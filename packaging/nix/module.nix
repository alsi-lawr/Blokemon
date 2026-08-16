{ self }:
{
  config,
  lib,
  pkgs,
  ...
}:
let
  cfg = config.services.blokemon-server;
  stateDir = "/var/lib/blokemon";
in
{
  # Drafted for the stage-two server deployment. The demo-MVP release serves the
  # standalone browser build as a static root, so this service stays disabled
  # until the authentication milestone gives the hosted profile store a purpose.
  options.services.blokemon-server = {
    enable = lib.mkEnableOption "the hosted Blokemon server";

    package = lib.mkOption {
      type = lib.types.package;
      default = self.packages.${pkgs.stdenv.hostPlatform.system}.blokemon-server;
      defaultText = lib.literalExpression "inputs.blokemon.packages.\${pkgs.stdenv.hostPlatform.system}.blokemon-server";
      description = "Blokemon server package to run.";
    };

    listenAddress = lib.mkOption {
      type = lib.types.str;
      default = "127.0.0.1";
      description = "Address on which the Blokemon server listens.";
    };

    port = lib.mkOption {
      type = lib.types.port;
      default = 8082;
      description = "TCP port on which the Blokemon server listens.";
    };

    openFirewall = lib.mkEnableOption "the Blokemon server port in the firewall";

    economyMode = lib.mkOption {
      type = lib.types.enum [
        "Unlimited"
        "ClassicScarcity"
      ];
      default = "Unlimited";
      description = "Economy mode the server starts with, matching the shipped browser default.";
    };

    environment = lib.mkOption {
      type = lib.types.attrsOf (
        lib.types.oneOf [
          lib.types.str
          lib.types.int
          lib.types.bool
        ]
      );
      default = { };
      example = {
        Blokemon__Economy__PackAllowance = 10;
      };
      description = ''
        Non-secret ASP.NET Core environment settings for the Blokemon server.
        These values are stored in the world-readable Nix store; use
        environmentFile for credentials and other secrets.
      '';
    };

    environmentFile = lib.mkOption {
      type = lib.types.nullOr lib.types.path;
      default = null;
      example = "/run/secrets/blokemon.env";
      description = ''
        Environment file containing secrets, as described by
        {manpage}`systemd.exec(5)`. The file must be readable by the blokemon
        service user and should not be created in the Nix store.
      '';
    };
  };

  config = lib.mkIf cfg.enable {
    users.groups.blokemon = { };
    users.users.blokemon = {
      isSystemUser = true;
      group = "blokemon";
      home = stateDir;
    };

    networking.firewall.allowedTCPPorts = lib.optional cfg.openFirewall cfg.port;

    systemd.tmpfiles.rules = [ "d ${stateDir} 0700 blokemon blokemon -" ];

    systemd.services.blokemon-server = {
      description = "Blokemon hosted server";
      after = [ "network-online.target" ];
      wants = [ "network-online.target" ];
      wantedBy = [ "multi-user.target" ];

      environment =
        lib.mapAttrs (
          _: value: if lib.isBool value then lib.boolToString value else toString value
        ) cfg.environment
        // {
          ASPNETCORE_ENVIRONMENT = "Production";
          # The shipped appsettings.json pins Urls, and application configuration
          # outranks ASPNETCORE_URLS, so the listen address has to be overridden
          # through the Urls key itself.
          Urls = "http://${cfg.listenAddress}:${toString cfg.port}";
          Blokemon__DataDirectory = stateDir;
          Blokemon__Economy__Mode = cfg.economyMode;
        };

      serviceConfig = {
        ExecStart = lib.getExe cfg.package;
        User = "blokemon";
        Group = "blokemon";
        WorkingDirectory = stateDir;
        Restart = "always";
        RestartSec = 5;
        UMask = "0077";

        NoNewPrivileges = true;
        PrivateTmp = true;
        ProtectHome = true;
        ProtectSystem = "strict";
        ReadWritePaths = [ stateDir ];
      }
      // lib.optionalAttrs (cfg.environmentFile != null) {
        EnvironmentFile = cfg.environmentFile;
      };
    };
  };
}
