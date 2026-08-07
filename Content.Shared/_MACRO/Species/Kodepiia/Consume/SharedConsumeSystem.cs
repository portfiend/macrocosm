using Content.Shared._MACRO.Species.Kodepiia.Consume.Components;
using Content.Shared.Actions;
using Content.Shared.DoAfter;
using Robust.Shared.Serialization;

namespace Content.Shared._MACRO.Species.Kodepiia.Consume;
/// <summary>
/// System that handles entities that consume other entities.. It's entity cannibalism.
/// </summary>
public abstract partial class SharedConsumeSystem : EntitySystem
{

    [Dependency] private SharedActionsSystem _actionsSystem = default!;
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ConsumeActionComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<ConsumeActionComponent, ComponentShutdown>(OnShutdown);
    }

    private void OnShutdown(Entity<ConsumeActionComponent> ent, ref ComponentShutdown args)
    {
        _actionsSystem.RemoveAction(ent.Owner, ent.Comp.ConsumeAction);
    }

    private void OnStartup(Entity<ConsumeActionComponent> ent, ref ComponentStartup args)
    {
        _actionsSystem.AddAction(ent, ref ent.Comp.ConsumeAction, ent.Comp.ConsumeActionId);
    }
}

/// <summary>
/// Event that is triggered when the entity uses the consume action.
/// </summary>
public sealed partial class ConsumeEvent : EntityTargetActionEvent;

/// <summary>
/// This is a consume doafter event! It is a simple doafter event!
/// </summary>
[Serializable, NetSerializable]
public sealed partial class ConsumeDoAfterEvent : SimpleDoAfterEvent;
