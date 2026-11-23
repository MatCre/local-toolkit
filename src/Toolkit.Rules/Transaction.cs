namespace Toolkit.Rules;

/// <summary>One row from a bank's exported CSV: a date, the payee/description text, and a signed amount.</summary>
public sealed record Transaction(DateOnly Date, string Description, decimal Amount);
