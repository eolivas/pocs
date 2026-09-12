---
inclusion: manual
---

# Code Smells & Anti-Patterns

This document is a consolidated reference for recognizing and fixing common code smells in .NET and React. For domain-specific anti-patterns (EF Core, microservices, REST, messaging), see the relevant steering files.

## Object-Oriented Code Smells

### God Class

A class that knows too much or does too much.

**Symptoms:**
- Class has 500+ lines
- Class has 10+ dependencies injected
- Class name ends in "Manager", "Helper", "Utility", "Service" without specificity
- Multiple unrelated public methods

**In this project:**
```csharp
// SMELL: Handler doing everything
public class {Entity}Service
{
    public async Task Place(...) { /* validation + persistence + notification + logging */ }
    public async Task Cancel(...) { /* same mess */ }
    public async Task Ship(...) { /* same mess */ }
    public async Task<{Entity}Dto> Get(...) { /* query + mapping */ }
    public async Task<List<{Entity}Dto>> List(...) { /* query + mapping */ }
}

// FIX: One handler per operation
public class Place{Entity}Handler : IRequestHandler<Place{Entity}Command, {Entity}Id> { }
public class Cancel{Entity}Handler : IRequestHandler<Cancel{Entity}Command, Unit> { }
public class Get{Entity}Handler : IRequestHandler<Get{Entity}Query, {Entity}Dto?> { }
```

**Rule:** If a class has more than 3-4 public methods doing unrelated work, split by responsibility.

---

### Feature Envy

A method that uses more data from another class than its own.

**Symptoms:**
- Chained property access: `order.Customer.Address.City`
- Method repeatedly accesses fields of another object to compute something

```csharp
// SMELL: Handler computing domain logic that belongs in the aggregate
public class Place{Entity}Handler
{
    public async Task Handle(...)
    {
        var total = entity.Lines.Sum(l => l.Quantity * l.UnitPrice.Amount); // Belongs in {Entity}!
        if (total > 10000m) throw new Exception("Limit exceeded");
    }
}

// FIX: Domain logic in the domain
public class {Entity} : AggregateRoot<{Entity}Id>
{
    public Money Total => new(_lines.Sum(l => l.LineTotal.Amount), _lines[0].UnitPrice.Currency);

    public void Place()
    {
        if (Total.Amount > 10000m)
            throw new {Entity}DomainException("Limit exceeded.");
    }
}
```

**Rule:** If a method accesses 3+ properties of another object to compute something, that logic probably belongs on the other object.

---

### Primitive Obsession

Using primitives (string, Guid, int) instead of domain types.

**Symptoms:**
- `Guid customerId` passed everywhere instead of `CustomerId`
- `string status` instead of an enum or state machine
- `decimal amount` without currency context

```csharp
// SMELL: Primitives for domain concepts
public record Place{Entity}Command(Guid CustomerId, List<LineDto> Lines);
// Any Guid could be passed — no type safety

// FIX: Strongly-typed IDs
public record Place{Entity}Command(CustomerId CustomerId, List<LineDto> Lines);
// Compiler prevents passing an OrderId where CustomerId is expected
```

**Rule:** Every domain identifier gets a `readonly record struct`. Every domain concept with rules gets a value object.

---

### Long Parameter List

Methods with 4+ parameters.

**Symptoms:**
- `Create(Guid id, string name, decimal amount, string currency, int quantity, DateTime date)`
- Boolean flags changing method behavior

```csharp
// SMELL
public static {Entity}Line Create(Guid productId, int quantity, decimal amount, string currency)

// FIX: Use value objects and strongly-typed IDs
public static {Entity}Line Create(ProductId productId, int quantity, Money unitPrice)
```

**Rule:** Group related parameters into value objects. If a method needs 5+ parameters, the abstraction is missing a concept.

---

### Shotgun Surgery

A single change requires modifying many unrelated classes.

**Symptoms:**
- Adding a new field requires changes in: entity, configuration, DTO, command, validator, handler, endpoint, test
- Adding a new status requires changes in 10+ files

**Mitigation in this project:**
- Pipeline behaviours prevent cross-cutting concern duplication
- Assembly scanning (validators, configurations, consumers) auto-discovers new classes
- Static `From()` mapping methods localize DTO changes
- MassTransit auto-configures queues for new consumers

**Rule:** If a routine change touches more than 4 files, look for a missing abstraction or automation.

---

### Dead Code

Code that is never executed.

**Symptoms:**
- Commented-out code blocks
- Methods never called (no references)
- `if` branches that can never be true
- Parameters that are always the same value
- Unused `using` statements

```csharp
// SMELL
public async Task Handle(...)
{
    // var oldImplementation = await _repo.GetLegacy(id); // TODO: remove after migration
    var entity = await _repo.GetByIdAsync(id, ct);
    // if (false) { ... }
}
```

**Rule:** Delete dead code. It's in version control if you ever need it back.

---

### Speculative Generality

Abstractions built for hypothetical future needs.

**Symptoms:**
- `IRepository<T>` base with one implementation
- `AbstractFactory` with one concrete factory
- Configuration options that only ever have one value
- Interface with methods no one calls yet

```csharp
// SMELL: Generic repository that adds no value
public interface IRepository<T> where T : AggregateRoot<T>
{
    Task<T?> GetByIdAsync(Guid id, CancellationToken ct);
    Task SaveAsync(T entity, CancellationToken ct);
    Task DeleteAsync(Guid id, CancellationToken ct);  // No one deletes anything
    Task<IList<T>> FindAsync(ISpecification<T> spec, CancellationToken ct); // One query uses this
}

// FIX: Specific interface per aggregate with only the methods handlers need
public interface I{Entity}Repository
{
    Task<{Entity}?> GetByIdAsync({Entity}Id id, CancellationToken ct);
    Task SaveAsync({Entity} entity, CancellationToken ct);
}
```

**Rule:** YAGNI — build abstractions when the second or third use case appears, not before.

---

## Architectural Anti-Patterns

### Anemic Domain Model

Entities are pure data holders — all logic lives in handlers or services.

**Symptoms:**
- Entities with only public getters/setters
- All business rules in Application handlers
- No domain events raised from entities
- Status transitions managed outside the aggregate

```csharp
// SMELL: Anemic entity
public class {Entity}
{
    public {Entity}Id Id { get; set; }
    public {Entity}Status Status { get; set; } // Public setter — anyone can change status!
    public List<{Entity}Line> Lines { get; set; } = new();
}

// Handler has all the rules
public class Place{Entity}Handler
{
    public async Task Handle(...)
    {
        entity.Status = {Entity}Status.Placed; // No invariant checking!
    }
}

// FIX: Rich domain model
public class {Entity} : AggregateRoot<{Entity}Id>
{
    public {Entity}Status Status { get; private set; }

    public void Place()
    {
        if (Status != {Entity}Status.Pending)
            throw new {Entity}DomainException("Only pending entities can be placed.");
        Status = {Entity}Status.Placed;
        RaiseDomainEvent(new {Entity}PlacedEvent(Id));
    }
}
```

**Rule:** Entities encapsulate their own invariants. Handlers orchestrate; they don't implement business rules.

---

### Service Locator

Resolving dependencies at runtime instead of injecting them.

**Symptoms:**
- `IServiceProvider` injected into classes
- `serviceProvider.GetService<T>()` inside business logic
- Hidden dependencies not visible in constructor

```csharp
// SMELL
public class Place{Entity}Handler
{
    private readonly IServiceProvider _sp;
    public Place{Entity}Handler(IServiceProvider sp) { _sp = sp; }

    public async Task Handle(...)
    {
        var repo = _sp.GetRequiredService<I{Entity}Repository>(); // Hidden dependency!
    }
}

// FIX: Explicit constructor injection
public class Place{Entity}Handler
{
    private readonly I{Entity}Repository _repo;
    public Place{Entity}Handler(I{Entity}Repository repo) { _repo = repo; }
}
```

**Rule:** Never inject `IServiceProvider` into handlers, domain services, or infrastructure classes. The only acceptable use is in composition roots and factory patterns.

---

### Leaky Abstraction

Infrastructure details leaking into upper layers.

**Symptoms:**
- `DbContext` injected into handlers
- `IQueryable<T>` returned from repositories
- EF Core `Include()` called in Application layer
- SQL strings in Application or Domain code

```csharp
// SMELL: Handler depends on EF Core
public class Get{Entity}Handler
{
    private readonly {SolutionName}DbContext _db;

    public async Task Handle(...)
    {
        var entity = await _db.Set<{Entity}>()
            .AsNoTracking()
            .Include(e => e.Lines) // EF Core concern in Application layer!
            .FirstOrDefaultAsync(e => e.Id == id);
    }
}

// FIX: Repository hides persistence details
public class Get{Entity}Handler
{
    private readonly I{Entity}Repository _repo;

    public async Task Handle(...)
    {
        var entity = await _repo.GetByIdAsync(id, ct); // No EF Core knowledge here
    }
}
```

**Rule:** Application and Domain layers have zero knowledge of EF Core, MassTransit, or HTTP clients.

---

## React Code Smells

### Prop Drilling

Passing props through many component layers that don't use them.

```tsx
// SMELL: Intermediate components pass props they don't use
<App user={user}>
  <Layout user={user}>
    <Sidebar user={user}>
      <UserBadge user={user} />  {/* Only this component needs it */}
    </Sidebar>
  </Layout>
</App>

// FIX: Use Zustand store or React context
const user = useAuthStore((s) => s.user);
```

**Rule:** If a prop passes through 2+ intermediate components that don't use it, extract to a store or context.

---

### God Component

A single component handling rendering, state, data fetching, and business logic.

```tsx
// SMELL: Component doing everything
export function OrderPage() {
  const [data, setData] = useState(null);
  const [error, setError] = useState(null);
  const [loading, setLoading] = useState(true);

  useEffect(() => { fetch('/api/leadsmicroservice').then(...).catch(...) }, []);

  const handleSubmit = async () => { /* validation + API call + error handling */ };

  return (/* 200 lines of JSX */);
}

// FIX: Separate concerns
export function OrderPage() {
  const { data, isLoading, error } = useLeadsMicroservice();       // Data fetching → custom hook
  const mutation = usePlaceOrder();                       // Mutations → TanStack Query

  if (isLoading) return <LoadingIndicator />;
  if (error) return <div role="alert">{error.message}</div>;

  return <OrderList leadsmicroservice={data} onPlace={mutation.mutate} />;
}
```

**Rule:** Components render. Hooks manage state and side effects. API hooks live in `api/index.ts`.

---

### Unnecessary State

State that can be derived from other state or props.

```tsx
// SMELL: Redundant state
const [items, setItems] = useState([]);
const [itemCount, setItemCount] = useState(0); // Derivable!

// Every time items changes, you must remember to update itemCount too.

// FIX: Derive it
const [items, setItems] = useState([]);
const itemCount = items.length; // Always in sync, zero maintenance
```

**Rule:** If a value can be computed from existing state or props, don't put it in `useState`.

---

### useEffect for Synchronous Derivations

```tsx
// SMELL: Effect to derive state
const [firstName, setFirstName] = useState('');
const [lastName, setLastName] = useState('');
const [fullName, setFullName] = useState('');

useEffect(() => {
  setFullName(`${firstName} ${lastName}`);
}, [firstName, lastName]);

// FIX: Just compute it during render
const fullName = `${firstName} ${lastName}`;
```

**Rule:** `useEffect` is for side effects (API calls, subscriptions, DOM mutations). Derivations are just variables.

---

## Detection Checklist

During code review, watch for these signals:

| Signal | Likely Smell | Fix |
|--------|-------------|-----|
| Class > 300 lines | God Class | Split by responsibility |
| Method > 30 lines | Long Method | Extract methods with descriptive names |
| 4+ constructor parameters | God Class or missing abstraction | Introduce a mediator or split the class |
| `string` for IDs | Primitive Obsession | Strongly-typed ID |
| `if/else` with 5+ branches | Replace conditional with polymorphism | Strategy pattern or state machine |
| Same code in 3+ places | Duplication | Extract to shared method or class |
| Commented-out code | Dead Code | Delete it |
| `// TODO` without issue link | Dead TODO | Link to an issue or delete |
| `catch (Exception) { }` | Swallowed exception | Log or rethrow |
| `public` on everything | Missing encapsulation | `private`/`internal` by default |
| `any` type in TypeScript | Missing type | Define a proper interface |
| `useEffect` with setState of derived values | Unnecessary effect | Compute during render |
