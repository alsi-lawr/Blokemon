namespace Blokemon.PackGen.Domain;

/// <summary>The printed size of a wrapper.</summary>
public enum WrapperSize
{
    /// <summary>The 460 by 830 booster wrapper.</summary>
    Booster,

    /// <summary>The 340 by 600 channel-pack wrapper.</summary>
    Small,
}

/// <summary>The construction a pack is built as.</summary>
public abstract record PackFormat
{
    private PackFormat() { }

    /// <summary>Applies the function matching this construction.</summary>
    /// <typeparam name="TResult">The result of the applied function.</typeparam>
    /// <param name="wrapper">Applied to a wrapper.</param>
    /// <param name="carton">Applied to a carton.</param>
    /// <returns>The result of the applied function.</returns>
    public abstract TResult Match<TResult>(
        Func<Wrapper, TResult> wrapper,
        Func<Carton, TResult> carton
    );

    /// <summary>A flexible wrapper crimped at both ends.</summary>
    /// <param name="Size">The printed wrapper size.</param>
    /// <param name="Resealable">Whether the wrapper carries a hang tab and a zip.</param>
    public sealed record Wrapper(WrapperSize Size, bool Resealable) : PackFormat
    {
        /// <inheritdoc/>
        public override TResult Match<TResult>(
            Func<Wrapper, TResult> wrapper,
            Func<Carton, TResult> carton
        ) => wrapper(this);
    }

    /// <summary>A rigid carton standing in perspective.</summary>
    public sealed record Carton : PackFormat
    {
        /// <inheritdoc/>
        public override TResult Match<TResult>(
            Func<Wrapper, TResult> wrapper,
            Func<Carton, TResult> carton
        ) => carton(this);
    }
}
