namespace Blokemon.Product;

public abstract record DomainResult<TValue, TFailure>
    where TValue : notnull
    where TFailure : notnull
{
    private DomainResult() { }

    public bool IsSuccess => this is Succeeded;

    public bool IsFailure => this is Failed;

    public abstract TResult Match<TResult>(
        Func<TValue, TResult> onSuccess,
        Func<TFailure, TResult> onFailure
    );

    public static DomainResult<TValue, TFailure> Success(TValue value) => new Succeeded(value);

    public static DomainResult<TValue, TFailure> Failure(TFailure failure) => new Failed(failure);

    public sealed record Succeeded(TValue Value) : DomainResult<TValue, TFailure>
    {
        public override TResult Match<TResult>(
            Func<TValue, TResult> onSuccess,
            Func<TFailure, TResult> onFailure
        ) => onSuccess(Value);
    }

    public sealed record Failed(TFailure Error) : DomainResult<TValue, TFailure>
    {
        public override TResult Match<TResult>(
            Func<TValue, TResult> onSuccess,
            Func<TFailure, TResult> onFailure
        ) => onFailure(Error);
    }
}
