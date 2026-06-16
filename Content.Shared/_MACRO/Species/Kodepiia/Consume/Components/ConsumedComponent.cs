using Robust.Shared.GameStates;

namespace Content.Shared._MACRO.Species.Kodepiia.Consume.Components;
/// <summary>
/// Entities with this component are considered "consumed" and track how many times they've been bitten.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class ConsumedComponent : Component
{
    /// <summary>
    /// How consumed this entity is, incremented by one every time they're consumed.
    /// </summary>
    [DataField, AutoNetworkedField]
    public float ConsumedValue;
}
