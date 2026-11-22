using System.ComponentModel.DataAnnotations;

namespace TkEndpoint.Api.TkItems;

/// <summary>
/// The body of POST /tkendpoint. The attributes are the validation rules: AddValidation() in
/// Program.cs checks them before the endpoint runs and answers 400 if any fail.
/// </summary>
public sealed class CreateTkItemRequest
{
    [Required, StringLength(100)]
    public string Name { get; init; } = "";

    [Range(1, 1000)]
    public int Quantity { get; init; }
}
