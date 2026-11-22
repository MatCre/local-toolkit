using Toolkit.Generators;

namespace Toolkit.Generators.Tests.Sample;

// This file is a consumer of the generator, like any project that uses it. Each one-line declaration
// below becomes a full type at compile time; the generated parts are in obj/generated/ and, in the
// IDE, under Dependencies → Analyzers → Toolkit.Generators.

[StronglyTypedId]
public partial record struct OrderId;

[StronglyTypedId(IdType.Int)]
public partial record struct CustomerNumber;

[StronglyTypedId(IdType.String)]
public partial record struct Sku;

/// <summary>With plain Guids and ints, nothing would stop Customer and Id being swapped. Here it won't compile.</summary>
public sealed record Order(OrderId Id, CustomerNumber Customer, Sku Sku);
