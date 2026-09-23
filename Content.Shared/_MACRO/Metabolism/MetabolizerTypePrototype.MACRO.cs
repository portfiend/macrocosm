using Content.Shared.EntityConditions.Conditions.Body;

namespace Content.Shared.Metabolism;

public sealed partial class MetabolizerTypePrototype
{
    /// <summary>
    ///     Whether or not this metabolizer type will show up in the guidebook
    ///     when included in a <seealso cref="MetabolizerTypeCondition"/>.
    /// </summary>
    [DataField]
    public bool ShowInGuidebook = true;
}
