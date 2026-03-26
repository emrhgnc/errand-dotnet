# Errand Architecture & Design

## 1. Messaging Contracts
- `IErrandRequest<TResponse>`: The message contract.
- `IErrandHandler<TRequest, TResponse>`: The contract for the fulfiller.

## 2. The Source Generator Strategy
Instead of calling `Assembly.GetTypes()` at runtime, Errand's Source Generator will:
- Scan for types implementing `IErrandHandler`.
- Generate a `StaticErrandDispatcher.g.cs` that provides a direct, non-reflective path to the handler's `HandleAsync` method.

## 3. Dispatching Mechanism
The `IErrandSender.SendAsync` method will not use `dynamic` or `MethodInfo.Invoke`. It should leverage the generated code to call the handler directly, ensuring maximum performance (JIT inlining friendly).

## 4. Pipeline Behaviors
Middleware/Behaviors must be pre-compiled into the execution chain during the source generation phase whenever possible.