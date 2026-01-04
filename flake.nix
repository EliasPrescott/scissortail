{
  description = "A very basic flake";

  inputs = {
    nixpkgs.url = "github:nixos/nixpkgs?ref=nixos-unstable";
    # I forked libtailscale because the current design of connection IDs being the socket FDs wasn't working for me.
    libtailscale.url = "github:EliasPrescott/libtailscale";
  };

  outputs = { self, nixpkgs, libtailscale }: let
    lib = nixpkgs.lib;
    defaultSystems = [
      "aarch64-darwin"
      "aarch64-linux"
      "x86_64-darwin"
      "x86_64-linux"
    ];
    eachDefaultSystem = lib.genAttrs defaultSystems;
  in {
    packages = eachDefaultSystem (system: let
      pkgs = nixpkgs.legacyPackages.${system};
    in rec {
      # https://learn.microsoft.com/en-us/nuget/create-packages/native-files-in-net-packages
      # Reference: https://github.com/NixOS/nixpkgs/blob/master/doc/languages-frameworks/dotnet.section.md
      class-library = pkgs.buildDotnetModule {
        pname = "Scissortail";
        version = "0.0.1";
        src = ./Scissortail;
        projectFile = "Scissortail.csproj";
        nugetDeps = ./Scissortail/deps.json;
        packNupkg = true;
        dotnet-sdk = pkgs.dotnetCorePackages.sdk_9_0;
        SCISSORTAIL_LIBTAILSCALE = "${libtailscale.packages.${system}.default}/bin/libtailscale";
      };

      default = pkgs.buildDotnetModule {
        pname = "Scissortail.MvcExample";
        version = "0.0.1";
        src = ./Scissortail.MvcExample;
        projectFile = "Scissortail.MvcExample.csproj";
        buildInputs = [ class-library ];
        nugetDeps = ./Scissortail.MvcExample/deps.json;
        dotnet-sdk = pkgs.dotnetCorePackages.sdk_9_0;
        dotnet-runtime = pkgs.dotnetCorePackages.aspnetcore_9_0;
        executables = [ "Scissortail.MvcExample" ];
        # https://discourse.nixos.org/t/building-asp-net-core-app-with-nix-serving-static-files/30810/4
        makeWrapperArgs = [
          "--set DOTNET_CONTENTROOT ${placeholder "out"}/lib/Scissortail.MvcExample"
        ];
      };

      container-image = pkgs.dockerTools.buildImage {
        name = "scissortail-mvc-example";
        tag = "0.0.1";
        config = {
          Cmd = [ "${default}/bin/Scissortail.MvcExample" ];
        };
        created = "now";
      };
    });
  };
}
