namespace Blokemon.App

open System
open System.Globalization
open Blokemon.Product
open Microsoft.Extensions.Configuration

// Both hosts resolve their economy from these settings: the server from its appsettings and
// environment, the browser build from the appsettings.json served beside its boot files.
module EconomyConfiguration =

    [<Literal>]
    let ModeKey = "Blokemon:Economy:Mode"

    [<Literal>]
    let PackAllowanceKey = "Blokemon:Economy:PackAllowance"

    let Resolve (configuration: IConfiguration) =
        ArgumentNullException.ThrowIfNull(configuration, nameof configuration)

        let configuredMode = configuration[ModeKey]

        if
            String.IsNullOrWhiteSpace configuredMode
            || String.Equals(
                configuredMode,
                nameof EconomyMode.Unlimited,
                StringComparison.OrdinalIgnoreCase
            )
        then
            EconomyRules.Unlimited
        else

            if
                not (
                    String.Equals(
                        configuredMode,
                        nameof EconomyMode.ClassicScarcity,
                        StringComparison.OrdinalIgnoreCase
                    )
                )
            then
                raise (
                    InvalidOperationException(
                        $"{ModeKey} must be {nameof EconomyMode.Unlimited} or "
                        + $"{nameof EconomyMode.ClassicScarcity}."
                    )
                )

            let configuredAllowance = configuration[PackAllowanceKey]

            let packAllowance =
                if String.IsNullOrWhiteSpace configuredAllowance then
                    EconomyRules.DefaultClassicPackAllowance
                else
                    match
                        Int32.TryParse(
                            configuredAllowance,
                            NumberStyles.Integer,
                            CultureInfo.InvariantCulture
                        )
                    with
                    | true, parsed -> parsed
                    | _ ->
                        raise (
                            InvalidOperationException($"{PackAllowanceKey} must be a whole number.")
                        )

            match EconomyRules.Classic packAllowance with
            | DomainResult.Succeeded rules -> rules
            | DomainResult.Failed _ ->
                raise (InvalidOperationException($"{PackAllowanceKey} must be zero or greater."))
