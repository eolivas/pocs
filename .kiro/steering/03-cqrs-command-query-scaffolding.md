---
inclusion: fileMatch
fileMatchPattern: "**/*Command*.cs,**/*Query*.cs,**/*Handler*.cs"
---

# CQRS Command/Query Scaffolding

This project uses MediatR for CQRS with FluentValidation and pipeline behaviours in `src/{SolutionName}.Application/`.

## Commands

### Command Definition (`Commands/{Name}Command.cs`)

```csharp
using MediatR;
using {SolutionName}.Domain;

namespace {SolutionName}.Application.Commands;

public record Place{Entity}Command : IRequest<{Entity}Id>
{
    public Guid CustomerId { get; init; }
    public IReadOnlyList<{Entity}LineDto> Lines { get; init; } = [];
}

// Inline DTO for command-specific input
public record {Entity}LineDto
{
    public Guid ProductId { get; init; }
    public int Quantity { get; init; }
    public decimal UnitPrice { get; init; }
    public string Currency { get; init; } = string.Empty;
}
```

Rules:
- Use `record` with `init` properties
- `IRequest<TResponse>` where TResponse is a strongly-typed ID or Unit
- Command-specific DTOs are co-located in the same file
- Naming: `{Verb}{Noun}Command`

### Command Validator (`Commands/{Name}CommandValidator.cs`)

```csharp
using FluentValidation;

namespace {SolutionName}.Application.Commands;

public class Place{Entity}CommandValidator : AbstractValidator<Place{Entity}Command>
{
    public Place{Entity}CommandValidator()
    {
        RuleFor(x => x.Lines)
            .NotEmpty()
            .WithMessage("A {entity} must contain at least one line.");
    }
}
```

Rules:
- One validator per command (not required for queries)
- Class name: `{CommandName}Validator`
- Validators are auto-registered via `AddValidatorsFromAssembly`
- The `ValidationBehaviour<,>` pipeline catches failures and throws `ValidationException`

### Command Handler (`Commands/{Name}Handler.cs`)

```csharp
using MediatR;
using {SolutionName}.Application.Interfaces;
using {SolutionName}.Domain;

namespace {SolutionName}.Application.Commands;

public class Place{Entity}Handler : IRequestHandler<Place{Entity}Command, {Entity}Id>
{
    private readonly I{Entity}Repository _repo;
    private readonly IApplicationEventPublisher _publisher;

    public Place{Entity}Handler(I{Entity}Repository repo, IApplicationEventPublisher publisher)
    {
        _repo = repo;
        _publisher = publisher;
    }

    public async Task<{Entity}Id> Handle(Place{Entity}Command request, CancellationToken cancellationToken)
    {
        // 1. Map DTOs to domain objects
        // 2. Create/load aggregate and invoke domain behaviour
        // 3. Persist via repository
        // 4. Publish domain events
        // 5. Return result
    }
}
```

Rules:
- Handler class name: `{Verb}{Noun}Handler`
- Inject repository interfaces (from Domain) and application interfaces (from Application/Interfaces)
- Never inject infrastructure types directly
- Publish domain events after persistence

## Queries

### Query Definition (`Queries/{Name}Query.cs`)

```csharp
using MediatR;
using {SolutionName}.Application.DTOs;

namespace {SolutionName}.Application.Queries;

public record Get{Entity}Query(Guid {Entity}Id) : IRequest<{Entity}Dto?>;
```

Rules:
- Use positional `record` parameters for simple queries
- Return DTOs (from `DTOs/`), never domain entities
- Naming: `Get{Noun}Query` or `List{Noun}Query`
- Nullable return (`{Entity}Dto?`) when the item might not exist

### Query Handler (`Queries/{Name}Handler.cs`)

```csharp
public class Get{Entity}Handler : IRequestHandler<Get{Entity}Query, {Entity}Dto?>
{
    private readonly I{Entity}Repository _repo;

    public Get{Entity}Handler(I{Entity}Repository repo)
    {
        _repo = repo;
    }

    public async Task<{Entity}Dto?> Handle(Get{Entity}Query request, CancellationToken cancellationToken)
    {
        var entity = await _repo.GetByIdAsync(new {Entity}Id(request.{Entity}Id), cancellationToken);
        return {Entity}Dto.From(entity);
    }
}
```

## Response DTOs (`DTOs/`)

```csharp
public record {Entity}Dto(
    Guid Id,
    Guid CustomerId,
    string Status,
    decimal TotalAmount,
    string TotalCurrency,
    IReadOnlyList<{Entity}LineDto> Lines)
{
    public static {Entity}Dto? From({Entity}? entity) { /* mapping logic */ }
}
```

Rules:
- Use positional `record` constructors
- Include a static `From(DomainEntity?)` mapping method
- Place in `src/{SolutionName}.Application/DTOs/`

## Pipeline Behaviours (`Behaviours/`)

Already wired in `Program.cs`:
1. `LoggingBehaviour<,>` — logs request/response
2. `ValidationBehaviour<,>` — runs FluentValidation validators

To add a new behaviour, create it in `Behaviours/` and register in `Program.cs`:
```csharp
cfg.AddBehavior(typeof(IPipelineBehavior<,>), typeof(YourBehaviour<,>));
```

## File Placement Summary

```
src/{SolutionName}.Application/
├── Commands/
│   ├── {Verb}{Noun}Command.cs       (command + inline DTOs)
│   ├── {Verb}{Noun}CommandValidator.cs
│   └── {Verb}{Noun}Handler.cs
├── Queries/
│   ├── {Get/List}{Noun}Query.cs
│   └── {Get/List}{Noun}Handler.cs
├── DTOs/
│   └── {Noun}Dto.cs
├── Behaviours/
│   └── {Name}Behaviour.cs
└── Interfaces/
    └── I{ServiceName}.cs
```
