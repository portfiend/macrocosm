using Content.Server.Atmos.Rotting;
using Content.Server.DoAfter;
using Content.Server.Fluids.EntitySystems;
using Content.Server.Forensics;
using Content.Server.Popups;
using Content.Shared._MACRO.CCVar;
using Content.Shared._MACRO.Species.Kodepiia.Consume;
using Content.Shared._MACRO.Species.Kodepiia.Consume.Components;
using Content.Shared.Body;
using Content.Shared.Body.Components;
using Content.Shared.Body.Systems;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Damage.Systems;
using Content.Shared.DoAfter;
using Content.Shared.FixedPoint;
using Content.Shared.Gibbing;
using Content.Shared.IdentityManagement;
using Content.Shared.Mobs.Systems;
using Content.Shared.Nutrition.EntitySystems;
using Content.Shared.Popups;
using Content.Shared.Whitelist;
using Robust.Server.Audio;
using Robust.Shared.Audio;
using Robust.Shared.Configuration;
using Robust.Shared.Physics.Components;
using Robust.Shared.Player;

namespace Content.Server._MACRO.Species.Kodepiia.Consume;
/// <inheritdoc/>
public sealed partial class ConsumeSystem : SharedConsumeSystem
{
    [Dependency] private IConfigurationManager _configurationManager = default!;

    [Dependency] private AudioSystem _audio = default!;
    [Dependency] private BodySystem _body = default!;
    [Dependency] private DamageableSystem _damage = default!;
    [Dependency] private DoAfterSystem _doAfter = default!;
    [Dependency] private EntityWhitelistSystem _whitelist = default!;
    [Dependency] private ForensicsSystem _forensics = default!;
    [Dependency] private GibbingSystem _gibbing = default!;
    [Dependency] private IngestionSystem _ingestion = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private PopupSystem _popup = default!;
    [Dependency] private PuddleSystem _puddle = default!;
    [Dependency] private RottingSystem _rotting = default!;
    [Dependency] private SharedSolutionContainerSystem _solutionContainer = default!;
    [Dependency] private StomachSystem _stomach = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ConsumeActionComponent, ConsumeEvent>(OnConsumeAction);
        SubscribeLocalEvent<ConsumeActionComponent, ConsumeDoAfterEvent>(OnConsumeDoAfter);

        SubscribeLocalEvent<BodyComponent, ConsumeActionEvent>(_body.RelayEvent);
        SubscribeLocalEvent<StomachComponent, BodyRelayedEvent<ConsumeActionEvent>>(OnConsumptionEvent);
    }

    private void OnConsumeAction(Entity<ConsumeActionComponent> ent, ref ConsumeEvent args)
    {
        // Check if we have a mouth.
        if (!_ingestion.HasMouthAvailable(args.Performer, args.Performer))
        {
            _popup.PopupClient(Loc.GetString(ent.Comp.ConsumeFailByBlock), ent, ent);
            return;
        }

        // Check if the target passes the whitelist and blacklist.
        if (!_whitelist.CheckBoth(args.Target, ent.Comp.Blacklist, ent.Comp.Whitelist))
        {
            _popup.PopupEntity(Loc.GetString(ent.Comp.ConsumeFailByInedible, ("target", Identity.Entity(args.Target, EntityManager))), ent, ent);
            return;
        }

        // Check if the entity is or is not incapacitated.
        if (!_mobState.IsIncapacitated(args.Target))
        {
            _popup.PopupEntity(Loc.GetString(ent.Comp.ConsumeFailByIncapacitated, ("target", Identity.Entity(args.Target, EntityManager))), ent, ent);
            return;
        }

        if (!TryComp<PhysicsComponent>(args.Target, out var targetPhysics))
            return;

        if (!TryComp<PhysicsComponent>(args.Performer, out var performerPhysics))
            return;

        // Setup the doafter.
        var doargs = new DoAfterArgs(EntityManager,
            ent,
            targetPhysics.Mass / performerPhysics.Mass * ent.Comp.BaseConsumeSpeed,
            new ConsumeDoAfterEvent(),
            ent,
            args.Target);

        // Do the popup for ourselves.
        if (ent.Comp.PopupSelfStart != null)
        {
            var popupSelf = Loc.GetString(ent.Comp.PopupSelfStart,
                ("user", Identity.Entity(ent, EntityManager)),
                ("target", Identity.Entity(args.Target, EntityManager)));
            _popup.PopupEntity(popupSelf, ent, ent);
        }

        // Do the popup for others.
        if (ent.Comp.PopupOthersStart != null)
        {
            var popupOthers = Loc.GetString(ent.Comp.PopupOthersStart,
                ("user", Identity.Entity(ent, EntityManager)),
                ("target", Identity.Entity(args.Target, EntityManager)));
            _popup.PopupEntity(popupOthers, ent, Filter.Pvs(ent).RemovePlayersByAttachedEntity(ent), true, PopupType.MediumCaution);
        }

        _doAfter.TryStartDoAfter(doargs);

        // Play our sound
        PlaySound(ent);

        args.Handled = true;
    }

    private void OnConsumeDoAfter(Entity<ConsumeActionComponent> ent, ref ConsumeDoAfterEvent args)
    {
        if (args.Target == null || args.Cancelled || !TryComp<PhysicsComponent>(args.Target, out var targetPhysics))
            return;

        var ev = new ConsumeActionEvent();
        RaiseLocalEvent(ent, ref ev);

        // All stomachs are full or we have no stomachs
        if (ev.LargestStomach.Comp == null)
        {
            _popup.PopupClient(Loc.GetString(ent.Comp.ConsumeFailByFullStomach, ("verb", "eat")), ent, ent);
            return;
        }

        if (ent.Comp.PopupSelfEnd != null)
        {
            var popupSelf = Loc.GetString(ent.Comp.PopupSelfEnd,
                ("user", Identity.Entity(ent, EntityManager)),
                ("target", Identity.Entity(args.Target.Value, EntityManager)));
            _popup.PopupEntity(popupSelf, ent, ent);
        }

        if (ent.Comp.PopupOthersEnd != null)
        {
            var popupOthers = Loc.GetString(ent.Comp.PopupOthersEnd,
                ("user", Identity.Entity(ent, EntityManager)),
                ("target", Identity.Entity(args.Target.Value, EntityManager)));
            _popup.PopupEntity(popupOthers, ent, Filter.Pvs(ent).RemovePlayersByAttachedEntity(ent), true, PopupType.MediumCaution);
        }

        Consume(ent, (args.Target.Value,targetPhysics), ev);
    }

    /// <summary>
    /// Have an entity consume another entity.
    /// </summary>
    /// <param name="consumer">Entity that consumes.</param>
    /// <param name="target">Entity that IS consumed.</param>
    /// <param name="consumeEvent">The event that lead this consumption.</param>
    private void Consume(Entity<ConsumeActionComponent> consumer, Entity<PhysicsComponent> target, ConsumeActionEvent consumeEvent)
    {
        // Drink Bloodstream
        _solutionContainer.TryGetSolution(target.Owner, consumer.Comp.SolutionToDrinkFrom, out var targetSolutionComp, out var targetBloodstream);
        if (targetBloodstream != null && targetSolutionComp != null)
        {
            var foodReagentQuantity = target.Comp.Mass * consumer.Comp.MeatMultiplier;

            var consumedSolution = _solutionContainer.SplitSolution(targetSolutionComp.Value, targetBloodstream.Volume * consumer.Comp.PortionDrunk);

            if (_rotting.IsRotten(target.Owner))
            {
                consumedSolution.AddReagent(consumer.Comp.Toxin, foodReagentQuantity * consumer.Comp.ToxinRatio);
                foodReagentQuantity *= 1 - consumer.Comp.ToxinRatio; // this math is bad i just know it
            }

            consumedSolution.AddReagent(consumer.Comp.FoodReagentPrototype, foodReagentQuantity);

            if (consumedSolution.Volume > consumeEvent.LargestVolume)
            {
                var split = consumedSolution.SplitSolution(consumedSolution.Volume - consumeEvent.LargestVolume);
                _puddle.TrySpillAt(consumer.Owner, split, out _);
            }
            _stomach.TryTransferSolution((consumeEvent.LargestStomach.Owner, consumeEvent.LargestStomach.Comp), consumedSolution);
        }

        // Ensure the victim has the consumed component
        EnsureComp<ConsumedComponent>(target.Owner, out var consumed);

        // Increment the consumed value on the victim.
        consumed.ConsumedValue += consumer.Comp.ConsumptionAmount;
        Dirty(target.Owner, consumed);

        var gibThreshold = _configurationManager.GetCVar(MacroCCVars.ConsumptionGibThreshold);

        // And finally, gib the victim if we've hit the threshold! If we don't gib, add whatever we need to the victim.
        if (gibThreshold != 0 && consumed.ConsumedValue >= gibThreshold)
            _gibbing.Gib(target.Owner);
        else
        {
            // Transfer DNA
            _forensics.TransferDna(target.Owner, consumer, false);

            // Deal Damage
            _damage.TryChangeDamage(target.Owner, consumer.Comp.Damage, true, false);

            // Play eat sound, don't need to play it if they gib because that's already a sound.
            PlaySound(consumer);
        }
    }

    private void OnConsumptionEvent(Entity<StomachComponent> ent, ref BodyRelayedEvent<ConsumeActionEvent> args)
    {
        if (!_solutionContainer.ResolveSolution(ent.Owner, StomachSystem.DefaultSolutionName, ref ent.Comp.Solution, out var stomachSol))
            return;

        if (stomachSol.AvailableVolume <= args.Args.LargestVolume)
            return;

        args.Args = new ConsumeActionEvent(LargestStomach: ent, LargestVolume: stomachSol.AvailableVolume);
    }

    /// <summary>
    /// Play the consume sound defined by an entity.
    /// </summary>
    /// <param name="ent">Entity to get the sound from and to play on.</param>
    private void PlaySound(Entity<ConsumeActionComponent> ent)
    {
        _audio.PlayPvs(ent.Comp.ConsumptionSound, ent, AudioParams.Default.WithVolume(-3f));
    }
}

/// <summary>
/// Raised when an entity consumes another entity.
/// </summary>
[ByRefEvent]
public record struct ConsumeActionEvent(Entity<StomachComponent> LargestStomach, FixedPoint2 LargestVolume);
