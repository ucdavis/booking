namespace Server.Core.Forms;

public sealed class FormValidation
{
    public bool? Required { get; init; }
    public int? MinLength { get; init; }
    public int? MaxLength { get; init; }
}
