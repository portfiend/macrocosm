using System.Diagnostics.CodeAnalysis;
using Content.Shared._MACRO.CCVar;
using Content.Shared._MACRO.Species.Kodepiia.Consume.Components;
using Content.Shared.Actions;
using Content.Shared.Atmos.Rotting;
using Content.Shared.Body;
using Content.Shared.Body.Components;
using Content.Shared.Body.Systems;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Damage.Systems;
using Content.Shared.DoAfter;
using Content.Shared.FixedPoint;
using Content.Shared.Fluids;
using Content.Shared.Forensics.Systems;
using Content.Shared.Gibbing;
using Content.Shared.IdentityManagement;
using Content.Shared.Mobs.Systems;
using Content.Shared.Nutrition.EntitySystems;
using Content.Shared.Popups;
using Content.Shared.Whitelist;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Configuration;
using Robust.Shared.Physics.Components;
using Robust.Shared.Player;
using Robust.Shared.Serialization;

namespace Content.Shared._MACRO.Species.Kodepiia.Consume;

/// <summary>
///     System that handles entities that consume other entities.. It's entity cannibalism.
/// </summary>
public abstract partial class SharedConsumeSystem : EntitySystem
{
    [Dependency] private IConfigurationManager _config = default!;

    [Dependency] private SharedActionsSystem _actions = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private BodySystem _body = default!;
    [Dependency] private DamageableSystem _damage = default!;
    [Dependency] private SharedDoAfterSystem _doAfter = default!;
    [Dependency] private EntityWhitelistSystem _whitelist = default!;
    [Dependency] private SharedForensicsSystem _forensics = default!;
    [Dependency] private GibbingSystem _gibbing = default!;
    [Dependency] private IngestionSystem _ingestion = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedPuddleSystem _puddle = default!;
    [Dependency] private SharedRottingSystem _rotting = default!;
    [Dependency] private SharedSolutionContainerSystem _solutionContainer = default!;
    [Dependency] private StomachSystem _stomach = default!;

    private int _gibThreshold;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<BodyComponent, ConsumeGetLargestStomachEvent>(_body.RelayEvent);

        Subs.CVar(_config,
            MacroCCVars.ConsumptionGibThreshold,
            value => _gibThreshold = value,
            invokeImmediately: true);
    }

    [SubscribeLocalEvent]
    private void OnStartup(Entity<ConsumeActionComponent> ent, ref ComponentStartup args)
    {
        _actions.AddAction(ent, ref ent.Comp.ConsumeAction, ent.Comp.ConsumeActionId);
    }

    [SubscribeLocalEvent]
    private void OnShutdown(Entity<ConsumeActionComponent> ent, ref ComponentShutdown args)
    {
        _actions.RemoveAction(ent.Owner, ent.Comp.ConsumeAction);
    }

    [SubscribeLocalEvent]
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
        PlayConsumeSound(ent);

        args.Handled = true;
    }

    [SubscribeLocalEvent]
    private void OnConsumeDoAfter(Entity<ConsumeActionComponent> ent, ref ConsumeDoAfterEvent args)
    {
        if (args.Target == null || args.Cancelled || !TryComp<PhysicsComponent>(args.Target, out var targetPhysics))
            return;

        var ev = new ConsumeGetLargestStomachEvent();
        RaiseLocalEvent(ent, ref ev);

        // All stomachs are full or we have no stomachs
        if (ev.LargestStomach == null)
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

        Consume(ent, (args.Target.Value, targetPhysics), ev.LargestStomach.Value.AsNullable());
    }

    /// <summary>
    /// Have an entity consume another entity.
    /// </summary>
    /// <param name="consumer">Entity that consumes.</param>
    /// <param name="target">Entity that IS consumed.</param>
    /// <param name="stomach">The consumer's largest available stomach.</param>
    private void Consume(Entity<ConsumeActionComponent> consumer,
        Entity<PhysicsComponent?> target,
        Entity<StomachComponent?> stomach)
    {
        IngestTargetContents(consumer, target, stomach);
        TakeABite(consumer, target);
    }

    /// <summary>
    ///     Make a consumer ingest a portion of blood and food reagents (e.g. uncooked proteins)
    ///     from a target entity.
    /// </summary>
    /// <param name="consumer">The entity that is consuming our target.</param>
    /// <param name="target">The poor guy who's getting nibbled on.</param>
    /// <param name="stomach">The consumer's largest available stomach.</param>
    private void IngestTargetContents(Entity<ConsumeActionComponent> consumer,
        Entity<PhysicsComponent?> target,
        Entity<StomachComponent?> stomach)
    {
        if (!Resolve(stomach.Owner, ref stomach.Comp))
            return;

        // Get the solution to ingest from the target.
        var consumedSolution = GetConsumedSolution(consumer, target);

        // Spill excess reagents on the floor.
        TryGetStomachSolution(stomach, out var stomachSol);
        var stomachVol = stomachSol?.AvailableVolume ?? 0.0f;
        if (consumedSolution.Volume > stomachVol)
        {
            var split = consumedSolution.SplitSolution(consumedSolution.Volume - stomachVol);
            _puddle.TrySpillAt(consumer.Owner, split, out _);
        }

        // Add the ingested solution to the stomach.
        _stomach.TryTransferSolution(stomach.AsNullable(), consumedSolution);
    }

    /// <summary>
    ///     Construct a portion of blood, food reagents, and potential toxins for our consumer to ingest
    ///     from a target's body.
    /// </summary>
    /// <param name="consumer">The entity that is consuming our target.</param>
    /// <param name="target">The poor guy who's getting nibbled on.</param>
    /// <returns>The solution to ingest on consumption.</returns>
    private Solution GetConsumedSolution(Entity<ConsumeActionComponent> consumer, Entity<PhysicsComponent?> target)
    {
        // The solution that our consumer is going to ingest.
        var consumedSolution = new Solution();

        // The quantity of food reagents (e.g. uncooked proteins) we are gonna ingest.
        var mass = Resolve(target.Owner, ref target.Comp)
            ? target.Comp.Mass
            : 0.0f;
        var ingestedFoodVolume = mass * consumer.Comp.MeatMultiplier;

        // Add toxin to the ingested solution if the target is rotting.
        if (_rotting.IsRotten(target.Owner))
        {
            var toxinVolume = ingestedFoodVolume * consumer.Comp.ToxinRatio;
            var cleanSolutionRatio = 1 - consumer.Comp.ToxinRatio;
            ingestedFoodVolume *= cleanSolutionRatio;
            consumedSolution.AddReagent(consumer.Comp.Toxin, toxinVolume); // yummers
        }

        // I take a sip
        if (_solutionContainer.TryGetSolution(target.Owner,
                consumer.Comp.SolutionToDrinkFrom,
                out var bloodSolutionComp,
                out var targetBloodstream))
        {
            var ingestedBloodVolume = targetBloodstream.Volume * consumer.Comp.PortionDrunk;
            var ingestedBlood = _solutionContainer.SplitSolution(bloodSolutionComp.Value, ingestedBloodVolume);
            consumedSolution.AddSolution(ingestedBlood, ProtoMan);
        }

        // Finally, food reagents.
        // We do this at the end because other factors might change this quantity.
        consumedSolution.AddReagent(consumer.Comp.FoodReagentPrototype, ingestedFoodVolume);

        return consumedSolution;
    }

    /// <summary>
    ///     Inflict a single consumption "bite" on a target, damaging the body.
    /// </summary>
    /// <param name="consumer">The entity consuming the target.</param>
    /// <param name="target">The target being consumed.</param>
    private void TakeABite(Entity<ConsumeActionComponent> consumer, EntityUid target)
    {
        // Increase consumption amount of the victim
        EnsureComp<ConsumedComponent>(target, out var consumed);
        consumed.ConsumedValue += consumer.Comp.ConsumptionAmount;
        Dirty(target, consumed);

        // Gib if we exceed the threshold
        if (_gibThreshold >= 0 && consumed.ConsumedValue >= _gibThreshold)
        {
            _gibbing.Gib(target);
            return;
        }

        // I take a bite
        _forensics.TransferDna(target, consumer, false);
        _damage.TryChangeDamage(target, consumer.Comp.Damage, true, false);
        PlayConsumeSound(consumer);
    }

    [SubscribeLocalEvent]
    private void OnConsumptionEvent(Entity<StomachComponent> ent, ref BodyRelayedEvent<ConsumeGetLargestStomachEvent> args)
    {
        if (!TryGetStomachSolution(ent.AsNullable(), out var stomachSol))
            return;

        // If this stomach is larger than the previous, then we replace the largest stomach with this one
        var largest = args.Args.LargestStomach;
        if (largest != null && TryGetStomachSolution(largest.Value.Owner, out var largestSol)
            && stomachSol.AvailableVolume > largestSol.AvailableVolume)
            args.Args = new ConsumeGetLargestStomachEvent(LargestStomach: ent);
    }

    private bool TryGetStomachSolution(Entity<StomachComponent?> ent, [NotNullWhen(true)] out Solution? solution)
    {
        solution = null;

        if (!Resolve(ent.Owner, ref ent.Comp)
            || _solutionContainer.ResolveSolution(ent.Owner,
            StomachSystem.DefaultSolutionName,
            ref ent.Comp.Solution,
            out solution))
            return false;

        return solution != null;
    }

    /// <summary>
    /// Play the consume sound defined by an entity.
    /// </summary>
    /// <param name="ent">Entity to get the sound from and to play on.</param>
    private void PlayConsumeSound(Entity<ConsumeActionComponent> ent)
    {
        _audio.PlayPvs(ent.Comp.ConsumptionSound, ent);
    }
}

/// <summary>
/// Raised when an entity consumes another entity.
/// </summary>
[ByRefEvent]
// TODO: ingestion system really needs a refactor huh
public record struct ConsumeGetLargestStomachEvent(Entity<StomachComponent>? LargestStomach);

/// <summary>
/// Event that is triggered when the entity uses the consume action.
/// </summary>
public sealed partial class ConsumeEvent : EntityTargetActionEvent;

/// <summary>
/// This is a consume doafter event! It is a simple doafter event!
/// </summary>
[Serializable, NetSerializable]
public sealed partial class ConsumeDoAfterEvent : SimpleDoAfterEvent;
