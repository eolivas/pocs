---
inclusion: fileMatch
fileMatchPattern: "**/*Mapper*.cs,**/*Dto*.cs,**/*Response*.cs,**/*Request*.cs"
---

# Object Mapping Conventions

This project uses **manual mapping** via static `From()` methods — no AutoMapper, no Mapster. This document covers why, where mappings live, patterns for each direction, and rules for keeping mappings maintainable.

## Why Manual Mapping

| Concern | AutoMapper | Manual (this project) |
|---------|------------|----------------------|
| Discoverability | Magic — "Find All References" doesn't work | Explicit — IDE navigation shows all callers |
| Compile-time safety | Runtime profile errors, silent null mappings | Compiler errors on type mismatches |
| Debugging | Hard to step through | Standard code — breakpoints work normally |
| Performance | Reflection-based (or source-generated) | Zero overhead — it's just assignments |
| Maintenance | Profile classes far from usage | Mapping lives on the DTO itself |
| Learning curve | Configure profiles, conventions, custom resolvers | None — it's just C# |

**Decision:** Manual mapping is simpler, faster, and easier to maintain for the scale of this project. AutoMapper adds value only for projects with 50+ mapping profiles and highly repetitive flat-to-flat transformations.

## Mapping Directions

```
API Request ──→ Application Command ──→ Domain Entity
                                              │
                                              ▼
API Response ←── Application DTO ←──── Domain Entity
```

| From | To | Direction | Where It Lives |
|------|----|-----------|----------------|
| API Request record | Application Command | Inbound | Endpoint (inline mapping) |
| Application Command | Domain Entity | Create/Modify | Handler (calls factory method) |
| Domain Entity | Application DTO | Outbound | Static `From()` on the DTO |
| Application DTO | API Response | Outbound | Usually same type (DTO IS the response) |

### Rules

- Domain entities are NEVER mapped directly to API responses — always go through a DTO
- DTOs NEVER contain domain logic — they're pure data carriers
- Mapping FROM domain → DTO lives on the DTO (`{Entity}Dto.From(entity)`)
- Mapping TO domain happens via domain factory methods, NOT via mapping code
- Request → Command mapping is trivial (inline in the endpoint)

## DTO Mapping Pattern (Domain → DTO)

### Basic Pattern

```csharp
// In src/{SolutionName}.Application/DTOs/{Entity}Dto.cs

public record {Entity}Dto(
    Guid Id,
    Guid CustomerId,
    string Status,
    decimal TotalAmount,
    string TotalCurrency,
    IReadOnlyList<{Entity}LineDto> Lines)
{
    public static {Entity}Dto? From({Entity}? entity)
    {
        if (entity is null) return null;

        return new {Entity}Dto(
            entity.Id.Value,
            entity.CustomerId.Value,
            entity.Status.ToString(),
            entity.Total.Amount,
            entity.Total.Currency,
            entity.Lines.Select({Entity}LineDto.From).ToList());
    }
}

public record {Entity}LineDto(
    Guid Id,
    Guid ProductId,
    int Quantity,
    decimal UnitPrice,
    string Currency)
{
    public static {Entity}LineDto From({Entity}Line line)
    {
        return new {Entity}LineDto(
            line.Id.Value,
            line.ProductId.Value,
            line.Quantity,
            line.UnitPrice.Amount,
            line.UnitPrice.Currency);
    }
}
```

### Rules for `From()` Methods

- Always `static` — no instance state needed
- Return nullable (`{Entity}Dto?`) when the source may be null
- Handle null explicitly at the top: `if (entity is null) return null;`
- Map child collections via `.Select(ChildDto.From).ToList()`
- Unwrap strongly-typed IDs: `entity.Id.Value`
- Unwrap value objects: `entity.Total.Amount`, `entity.Total.Currency`
- Convert enums to strings: `entity.Status.ToString()`

## Request → Command Mapping (Endpoint)

```csharp
group.MapPost("/", async (Place{Entity}Request request, ISender sender) =>
{
    var command = new Place{Entity}Command
    {
        CustomerId = request.CustomerId,
        Lines = request.Lines.Select(l => new {Entity}LineDto
        {
            ProductId = l.ProductId,
            Quantity = l.Quantity,
            UnitPrice = l.UnitPrice,
            Currency = l.Currency
        }).ToList()
    };

    var id = await sender.Send(command);
    return Results.Created($"/api/{entities}/{id.Value}", new { id = id.Value });
});
```

### Rules

- Mapping from API request to command happens inline in the endpoint
- Keep it simple — if it exceeds 10 lines, extract to a private static method
- Never create a separate "mapper class" for request → command (YAGNI)
- Validate at the command level (FluentValidation), not during mapping

## Command → Domain (Handler)

The handler does NOT "map" — it calls domain factory methods:

```csharp
public async Task<{Entity}Id> Handle(Place{Entity}Command request, CancellationToken ct)
{
    // Map command DTOs to domain value objects
    var lines = request.Lines.Select(l =>
        {Entity}Line.Create(
            new ProductId(l.ProductId),
            l.Quantity,
            new Money(l.UnitPrice, l.Currency)))
        .ToList();

    // Call domain factory (validates invariants)
    var entity = {Entity}.Create(new CustomerId(request.CustomerId), lines);

    await _repo.SaveAsync(entity, ct);
    return entity.Id;
}
```

### Rules

- Construct value objects and strongly-typed IDs from command primitive data
- Call domain `Create()` or `Update()` methods — they enforce invariants
- Never bypass domain factory methods to "map" directly into an entity
- This is not mapping — it's domain construction

## Collection Mapping

### Lists and Projections

```csharp
// Domain list → DTO list
public static IReadOnlyList<{Entity}Dto> FromList(IEnumerable<{Entity}> entities)
{
    return entities.Select(e => From(e)!).ToList();
}
```

### Paginated Results

```csharp
public record PagedResult<T>(
    IReadOnlyList<T> Data,
    int Page,
    int PageSize,
    int TotalCount)
{
    public int TotalPages => (int)Math.Ceiling(TotalCount / (double)PageSize);
    public bool HasNextPage => Page < TotalPages;
    public bool HasPreviousPage => Page > 1;
}

// Usage in handler
var entities = await _repo.GetPagedAsync(page, pageSize, ct);
var dtos = entities.Items.Select({Entity}Dto.From).ToList();
return new PagedResult<{Entity}Dto>(dtos, page, pageSize, entities.TotalCount);
```

## Null Handling

| Scenario | Convention |
|----------|-----------|
| Entity might not exist | Return `{Entity}Dto?` (nullable) |
| Collection might be empty | Return empty list `[]`, never null |
| Optional nested object | Map to null in DTO if source is null |
| Required field is missing | Throw during domain construction (not during mapping) |

```csharp
// Nullable nested mapping
public static {Entity}Dto? From({Entity}? entity)
{
    if (entity is null) return null; // Caller handles null (e.g., return 404)
    // ...
}
```

**Rule:** Mapping code never throws on null — it returns null. The caller decides how to handle it (404, empty list, default value).

## Frontend Mapping (TypeScript)

### API Response → Frontend Type

```typescript
// types.ts — matches the backend DTO shape
export interface {Entity}Dto {
  id: string;
  customerId: string;
  status: string;
  totalAmount: number;
  totalCurrency: string;
  lines: {Entity}LineDto[];
}

// The TanStack Query hook returns the API shape directly — no extra mapping layer
export function use{Entity}(id: string) {
  return useQuery<{Entity}Dto>({
    queryKey: ['{entities}', id],
    queryFn: async () => {
      const response = await http.get<{Entity}Dto>(`/{entities}/${id}`);
      return response.data; // Direct pass-through — API shape IS the frontend type
    },
  });
}
```

### When Frontend Mapping IS Needed

Only when the component needs a different shape than the API provides:

```typescript
// Transform for display purposes
function toDisplayModel(dto: {Entity}Dto): {Entity}DisplayModel {
  return {
    id: dto.id,
    statusLabel: formatStatus(dto.status),
    totalFormatted: `${dto.totalCurrency} ${dto.totalAmount.toFixed(2)}`,
    lineCount: dto.lines.length,
  };
}
```

### Rules

- Frontend types mirror backend DTO shapes — don't add an extra mapping layer
- Transform only for display (formatting, computed display values)
- Place display transformations in the component or a co-located utility
- Never create a "mapping service" class in the frontend

## Anti-Patterns

| Anti-Pattern | Problem | Fix |
|-------------|---------|-----|
| AutoMapper for 5 DTOs | Over-engineering, hidden mappings | Manual `From()` methods |
| Mapper class per entity | Extra file, indirection, no benefit | Static method on the DTO |
| Mapping inside domain entity | Domain knows about DTOs (layer violation) | DTO knows about domain, not vice versa |
| Shared DTO across endpoints | Different endpoints evolve differently | One DTO per use case if shapes diverge |
| Mapping in repository | Repository returns domain objects, not DTOs | Map in handler or query |
| `entity.ToDto()` extension | Pollutes domain with mapping concern | `Dto.From(entity)` keeps mapping on DTO side |

## When to Add a Dedicated Mapper

You DON'T need a separate mapper class until:
- The `From()` method exceeds 30 lines (complex nested transformations)
- Multiple DTOs map from the same entity differently (split into focused methods)
- Mapping requires injected services (e.g., URL generation, localization)

If you need injected services for mapping:
```csharp
// Rare — only when mapping requires runtime dependencies
public class {Entity}DtoFactory
{
    private readonly ILinkGenerator _links;

    public {Entity}DtoFactory(ILinkGenerator links) { _links = links; }

    public {Entity}Dto Create({Entity} entity)
    {
        return new {Entity}Dto(/* ... */, _links.GetUri("GetEntity", new { id = entity.Id.Value }));
    }
}
```

**Rule:** This is rare. For 95% of cases, static `From()` is sufficient.
