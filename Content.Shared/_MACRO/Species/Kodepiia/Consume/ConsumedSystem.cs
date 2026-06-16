using Content.Shared.Examine;
using Content.Shared.IdentityManagement;
using Content.Shared.Mobs;
using Content.Shared._MACRO.Species.Kodepiia.Consume.Components;

namespace Content.Shared._MACRO.Species.Kodepiia.Consume;

/// <summary>
/// This system handles entities that have been consumed by entites with the ConsumeActionComponent.
/// </summary>
public sealed partial class ConsumedSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ConsumedComponent, ExaminedEvent>(OnExamine);
        SubscribeLocalEvent<ConsumedComponent, MobStateChangedEvent>(OnMobStateChange);
    }

    private void OnExamine(Entity<ConsumedComponent> ent, ref ExaminedEvent args)
    {
        var consumeIndex = 0;
        //This is basically just how consumed the entity is, with a range of 1 to 4
        switch (ent.Comp.ConsumedValue)
        {
            case <= 1:
                consumeIndex = 1;
                break;
            case <= 3:
                consumeIndex = 2;
                break;
            case <= 4:
                consumeIndex = 3;
                break;
            case <= 8:
                consumeIndex = 4;
                break;
        }

        args.PushMarkup(Loc.GetString($"consumed-onexamine-{consumeIndex}",
            ("target", Identity.Entity(ent, EntityManager))));

    }

    private void OnMobStateChange(Entity<ConsumedComponent> ent, ref MobStateChangedEvent args)
    {
        // If the entity is like, revived, it should no longer be considered "consumed"
        if (args.NewMobState == MobState.Alive)
            RemComp<ConsumedComponent>(ent);
    }
}
