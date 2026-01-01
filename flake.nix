{
  description = "A very basic flake";

  inputs = {
    nixpkgs.url = "github:nixos/nixpkgs?ref=nixos-unstable";
    # I forked libtailscale because the current design of connection IDs being the socket FDs wasn't working for me.
    libtailscale.url = "github:EliasPrescott/libtailscale";
  };

  outputs = { self, nixpkgs, libtailscale }: let
    pkgs = nixpkgs.legacyPackages.aarch64-darwin;
  in rec {
    # Reference: https://github.com/NixOS/nixpkgs/blob/master/doc/languages-frameworks/dotnet.section.md
    packages.aarch64-darwin.class-library = pkgs.buildDotnetModule {
      pname = "Scissortail";
      version = "0.0.1";
      src = ./Scissortail;
      projectFile = "Scissortail.csproj";
      nugetDeps = ./Scissortail/deps.json;
      packNupkg = true;
      dotnet-sdk = pkgs.dotnetCorePackages.sdk_9_0;
      NIX_LIBS = "
        ${libtailscale.packages.aarch64-darwin.default}/bin/libtailscale
      ";
    };

    packages.aarch64-darwin.default = pkgs.buildDotnetModule {
      pname = "Scissortail.MvcExample";
      version = "0.0.1";
      src = ./Scissortail.MvcExample;
      projectFile = "Scissortail.MvcExample.csproj";
      buildInputs = [ packages.aarch64-darwin.class-library ];
      nugetDeps = ./Scissortail.MvcExample/deps.json;
      dotnet-sdk = pkgs.dotnetCorePackages.sdk_9_0;
      dotnet-runtime = pkgs.dotnetCorePackages.aspnetcore_9_0;
      executables = [ "Scissortail.MvcExample" ];
      NIX_LIBS = "
        ${libtailscale.packages.aarch64-darwin.default}/bin/libtailscale
      ";
      runtimeDeps = [
        "${libtailscale.packages.aarch64-darwin.default}/bin/libtailscale"
      ];
      # https://discourse.nixos.org/t/building-asp-net-core-app-with-nix-serving-static-files/30810/4
      makeWrapperArgs = [
        "--set DOTNET_CONTENTROOT ${placeholder "out"}/lib/Scissortail.MvcExample"
      ];
    };

    devShells.aarch64-darwin.default = pkgs.mkShell {
      name = "scissortail";
      nativeBuildInputs = [
        pkgs.pkg-config
      ];
      NIX_LIBS = "
        ${libtailscale.packages.aarch64-darwin.default}/bin
      ";
    };
  };
}
