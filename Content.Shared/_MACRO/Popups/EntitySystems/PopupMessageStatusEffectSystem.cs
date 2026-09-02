using Content.Shared._DEN.StatusEffects.Components;
using Content.Shared.EntityEffects.Effects.Transform;
using Content.Shared.StatusEffectNew;
using Content.Shared.StatusEffectNew.Components;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Shared._DEN.StatusEffects.EntitySystems;

/// <summary>
///     This system handles displaying popup messages for various popup message status effects.
/// </summary>
public sealed partial class PopupMessageStatusEffectSystem : EntitySystem
{
    [Dependency] private PopupMessageEntityEffectSystem _popupEffect = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private IGameTiming _timing = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<IntervalPopupMessageStatusEffectComponent, StatusEffectAppliedEvent>(OnIntervalPopupApplied);
        SubscribeLocalEvent<ExpiryPopupMessageStatusEffectComponent, StatusEffectRemovedEvent>(OnExpiredPopupRemoved);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<IntervalPopupMessageStatusEffectComponent, StatusEffectComponent>();
        while (query.MoveNext(out var uid, out var interval, out var statusEffect))
        {
            if (_timing.CurTime < interval.NextPopupTime)
                continue;

            UpdatePopupIntervalTime((uid, interval));
            SpawnPopup((uid, statusEffect), interval);
        }
    }

    private void OnIntervalPopupApplied(Entity<IntervalPopupMessageStatusEffectComponent> ent, ref StatusEffectAppliedEvent args)
    {
        UpdatePopupIntervalTime(ent);
    }

    private void OnExpiredPopupRemoved(Entity<ExpiryPopupMessageStatusEffectComponent> ent, ref StatusEffectRemovedEvent args)
    {
        SpawnPopup((ent.Owner, null), ent.Comp);
    }

    /// <summary>
    ///     Spawns a popup message related to a popup message status effect.
    /// </summary>
    /// <param name="ent">The status effect entity.</param>
    /// <param name="popupComp">The popup message component associated with this effect.</param>
    private void SpawnPopup(Entity<StatusEffectComponent?> ent, PopupMessageStatusEffectComponent popupComp)
    {
        if (!Resolve(ent.Owner, ref ent.Comp))
            return;

        var statusEffect = ent.Comp;
        if (statusEffect.AppliedTo == null)
            return;

        var xform = Transform(statusEffect.AppliedTo.Value);
        _popupEffect.PopupMessage((statusEffect.AppliedTo.Value, xform),
            popupComp.Messages,
            popupComp.VisualType,
            popupComp.Method,
            popupComp.Recipients);
    }

    /// <summary>
    ///     Sets the next popup message spawn time for an interval popup effect.
    /// </summary>
    /// <param name="ent">The interval popup status effect.</param>
    private void UpdatePopupIntervalTime(Entity<IntervalPopupMessageStatusEffectComponent> ent)
    {
        var comp = ent.Comp;
        var (min, max) = comp.Interval;
        var newInterval = _random.NextDouble(min.TotalSeconds, max.TotalSeconds);

        comp.NextPopupTime = _timing.CurTime + TimeSpan.FromSeconds(newInterval);
    }
}
