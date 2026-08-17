using System.Globalization;
using Blokemon.Product;
using Microsoft.Extensions.Configuration;

namespace Blokemon.App;

// Both hosts resolve their economy from these settings: the server from its appsettings and
// environment, the browser build from the appsettings.json served beside its boot files.
public static class EconomyConfiguration
{
    public const string ModeKey = "Blokemon:Economy:Mode";

    public const string PackAllowanceKey = "Blokemon:Economy:PackAllowance";

    public static EconomyRules Resolve(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var configuredMode = configuration[ModeKey];
        if (
            string.IsNullOrWhiteSpace(configuredMode)
            || string.Equals(
                configuredMode,
                nameof(EconomyMode.Unlimited),
                StringComparison.OrdinalIgnoreCase
            )
        )
        {
            return EconomyRules.Unlimited;
        }
        if (
            !string.Equals(
                configuredMode,
                nameof(EconomyMode.ClassicScarcity),
                StringComparison.OrdinalIgnoreCase
            )
        )
        {
            throw new InvalidOperationException(
                $"{ModeKey} must be {nameof(EconomyMode.Unlimited)} or "
                    + $"{nameof(EconomyMode.ClassicScarcity)}."
            );
        }

        var configuredAllowance = configuration[PackAllowanceKey];
        var packAllowance = EconomyRules.DefaultClassicPackAllowance;
        if (!string.IsNullOrWhiteSpace(configuredAllowance))
        {
            if (
                !int.TryParse(
                    configuredAllowance,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out packAllowance
                )
            )
            {
                throw new InvalidOperationException($"{PackAllowanceKey} must be a whole number.");
            }
        }

        return EconomyRules
            .Classic(packAllowance)
            .Match(
                static rules => rules,
                _ =>
                    throw new InvalidOperationException(
                        $"{PackAllowanceKey} must be zero or greater."
                    )
            );
    }
}
