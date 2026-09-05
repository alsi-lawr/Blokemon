{
  description = "Blokemon standalone trading card game: browser build and hosted server";

  inputs.nixpkgs.url = "github:NixOS/nixpkgs/nixos-unstable";

  outputs =
    { self, nixpkgs, ... }:
    let
      supportedSystems = [
        "x86_64-linux"
        "aarch64-linux"
        "aarch64-darwin"
      ];
      releaseVersion = "0.6.0";
      imageSource = "https://github.com/alsi-lawr/Blokemon";
      imageRevision = self.rev or self.dirtyRev or "unknown";
      forAllSystems = nixpkgs.lib.genAttrs supportedSystems;
      pkgsFor = system: import nixpkgs { inherit system; };
      developmentPackages = pkgs: [
        pkgs.dotnet-sdk_10
        pkgs.csharpier
        pkgs.fantomas
        pkgs.nixfmt
      ];
      commonSourceFiles = [
        ./Directory.Build.props
        ./Directory.Packages.props
        ./global.json
      ];
      siteSource =
        pkgs:
        pkgs.lib.fileset.toSource {
          root = ./.;
          fileset = pkgs.lib.fileset.unions (
            commonSourceFiles
            ++ [
              ./src/Blokemon.App
              ./src/Blokemon.App.Catalogue
              ./src/Blokemon.App.Client
              ./src/Blokemon.App.Contracts
              ./src/Blokemon.Core
              ./src/Blokemon.Core.Codecs
              ./src/Blokemon.Core.Contracts
              ./src/Blokemon.Cpu
              ./src/Blokemon.Game
              ./src/Blokemon.Product
              ./src/Blokemon.Web.Client
              # The browser build links the shared stylesheet and favicon out of
              # the server project, and the card art and fonts out of content.
              ./src/Blokemon.Web/wwwroot
              # The delivered illustrations only. The approved artwork the browser build
              # has no use for is seventy-two megabytes it would otherwise carry into
              # every evaluation of this source.
              ./content/art-web
              ./content/fonts
            ]
          );
        };
      serverSource =
        pkgs:
        pkgs.lib.fileset.toSource {
          root = ./.;
          fileset = pkgs.lib.fileset.unions (
            commonSourceFiles
            ++ [
              ./src/Blokemon.App
              ./src/Blokemon.App.Catalogue
              ./src/Blokemon.App.Client
              ./src/Blokemon.App.Contracts
              ./src/Blokemon.CardGen
              ./src/Blokemon.Core
              ./src/Blokemon.Core.Codecs
              ./src/Blokemon.Core.Contracts
              ./src/Blokemon.Cpu
              ./src/Blokemon.Game
              ./src/Blokemon.Identity.Federated
              ./src/Blokemon.PackGen
              ./src/Blokemon.Product
              ./src/Blokemon.Web
              ./src/Blokemon.Web.Client
              ./src/Blokemon.Web.Content
              # The server assembles the catalogue itself, so it needs the approved artwork
              # the set authority is read against as well as the form it serves.
              ./content/art
              ./content/art-web
              ./content/authorities
              ./content/fonts
            ]
          );
        };
      sitePackageFor =
        system:
        let
          pkgs = pkgsFor system;
        in
        pkgs.buildDotnetModule {
          pname = "blokemon-site";
          version = releaseVersion;
          src = siteSource pkgs;
          enableParallelBuilding = false;

          projectFile = "src/Blokemon.Web.Client/Blokemon.Web.Client.csproj";
          nugetDeps = ./packaging/nix/deps.json;
          dotnet-sdk = pkgs.dotnet-sdk_10;
          dotnet-runtime = pkgs.dotnet-aspnetcore_10;
          runtimeId = "browser-wasm";
          selfContainedBuild = true;
          useAppHost = false;
          executables = [ ];
          # Project-scoped standalone flavour: the browser host reads its economy
          # from wwwroot/appsettings.json and talks to no server.
          dotnetFlags = [ "-p:StandaloneBrowser=true" ];
          dotnetBuildFlags = [ "-p:SourceRevisionId=${imageRevision}" ];

          # The published Blazor WebAssembly wwwroot is the whole deliverable: it
          # is the static root a web server points at, so it becomes $out itself.
          postFixup = ''
            mv "$out/lib/blokemon-site/wwwroot" "$out/static-root"
            rm -rf "$out/lib"
            shopt -s dotglob
            mv "$out/static-root"/* "$out/"
            rmdir "$out/static-root"
            test -f "$out/index.html"
          '';

          meta = {
            description = "Blokemon standalone browser build served as a static site";
            platforms = pkgs.lib.platforms.unix;
          };
        };
      serverPackageFor =
        system:
        let
          pkgs = pkgsFor system;
        in
        pkgs.buildDotnetModule {
          pname = "blokemon-server";
          version = releaseVersion;
          src = serverSource pkgs;
          enableParallelBuilding = false;

          projectFile = "src/Blokemon.Web/Blokemon.Web.csproj";
          nugetDeps = ./packaging/nix/deps.json;
          dotnet-sdk = pkgs.dotnet-sdk_10;
          dotnet-runtime = pkgs.dotnet-aspnetcore_10;
          executables = [ "Blokemon.Web" ];
          makeWrapperArgs = [
            "--set-default"
            "ASPNETCORE_CONTENTROOT"
            "${placeholder "out"}/lib/blokemon-server"
          ];

          # The server references the Blazor WebAssembly client, whose runtime
          # pack only exists for browser-wasm. buildDotnetModule's hooks always
          # pass the host runtime identifier, which makes that reference demand a
          # Mono.linux-x64 pack that has never been published, so the server
          # drives a runtime-identifier-free restore, build, and publish instead.
          configurePhase = ''
            runHook preConfigure
            dotnet restore src/Blokemon.Web/Blokemon.Web.csproj \
              -p:ContinuousIntegrationBuild=true \
              -p:Deterministic=true \
              -p:NuGetAudit=false \
              --disable-parallel
            runHook postConfigure
          '';

          buildPhase = ''
            runHook preBuild
            dotnet build src/Blokemon.Web/Blokemon.Web.csproj \
              -maxcpucount:1 \
              -p:BuildInParallel=false \
              -p:ContinuousIntegrationBuild=true \
              -p:Deterministic=true \
              -p:OverwriteReadOnlyFiles=true \
              -p:InformationalVersion=${releaseVersion} \
              -p:SourceRevisionId=${imageRevision} \
              --configuration Release \
              --no-restore
            runHook postBuild
          '';

          installPhase = ''
            runHook preInstall
            dotnet publish src/Blokemon.Web/Blokemon.Web.csproj \
              -maxcpucount:1 \
              -p:ContinuousIntegrationBuild=true \
              -p:Deterministic=true \
              -p:OverwriteReadOnlyFiles=true \
              --output "$out/lib/blokemon-server" \
              --configuration Release \
              --no-restore \
              --no-build
            runHook postInstall
          '';

          postFixup = ''
            mv "$out/bin/Blokemon.Web" "$out/bin/blokemon-server"
          '';

          meta = {
            description = "Blokemon hosted server";
            mainProgram = "blokemon-server";
            platforms = pkgs.lib.platforms.unix;
          };
        };
      imageArchitectureFor =
        system:
        {
          x86_64-linux = "amd64";
          aarch64-linux = "arm64";
        }
        .${system} or (throw "Container images are unsupported on ${system}");
      imageTagFor = system: "${releaseVersion}-${imageArchitectureFor system}";
      imageLabels = title: {
        "org.opencontainers.image.source" = imageSource;
        "org.opencontainers.image.version" = releaseVersion;
        "org.opencontainers.image.revision" = imageRevision;
        "org.opencontainers.image.title" = title;
      };
      containerImagesFor =
        system:
        let
          pkgs = pkgsFor system;
          packages = self.packages.${system};
          architecture = imageArchitectureFor system;
          tag = imageTagFor system;
        in
        {
          blokemon-server-image = pkgs.dockerTools.buildLayeredImage {
            name = "ghcr.io/alsi-lawr/blokemon-server";
            inherit architecture tag;
            contents = [
              packages.blokemon-server
              pkgs.dockerTools.caCertificates
            ];
            fakeRootCommands = ''
              mkdir -p ./data ./tmp
              chown 65532:65532 ./data ./tmp
              chmod 0700 ./data ./tmp
            '';
            config = {
              User = "65532:65532";
              WorkingDir = "/data";
              Entrypoint = [ "${packages.blokemon-server}/bin/blokemon-server" ];
              Env = [
                "ASPNETCORE_ENVIRONMENT=Production"
                # The shipped appsettings.json pins Urls, and application
                # configuration outranks ASPNETCORE_URLS, so the listen address
                # has to be overridden through the Urls key itself.
                "Urls=http://0.0.0.0:8082"
                "Blokemon__DataDirectory=/data"
                "HOME=/data"
              ];
              ExposedPorts = {
                "8082/tcp" = { };
              };
              Volumes = {
                "/data" = { };
              };
              Labels = imageLabels "Blokemon server";
            };
          };
        };
    in
    {
      packages = forAllSystems (
        system:
        let
          pkgs = pkgsFor system;
          blokemon-site = sitePackageFor system;
          blokemon-server = serverPackageFor system;
        in
        {
          default = blokemon-site;
          server = blokemon-server;
          inherit blokemon-site blokemon-server;
        }
        // pkgs.lib.optionalAttrs pkgs.stdenv.hostPlatform.isLinux (containerImagesFor system)
      );

      apps = forAllSystems (
        system:
        let
          packages = self.packages.${system};
        in
        {
          default = self.apps.${system}.blokemon-server;
          blokemon-server = {
            type = "app";
            program = "${packages.blokemon-server}/bin/blokemon-server";
            meta.description = "Run the Blokemon server";
          };
        }
      );

      devShells = forAllSystems (
        system:
        let
          pkgs = pkgsFor system;
        in
        {
          default = pkgs.mkShellNoCC {
            packages = developmentPackages pkgs;
          };
        }
      );

      formatter = forAllSystems (system: (pkgsFor system).nixfmt);

      nixosModules = {
        default = self.nixosModules.blokemon-server;
        blokemon-server = import ./packaging/nix/module.nix { inherit self; };
      };
    };
}
