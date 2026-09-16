# See https://aka.ms/customizecontainer to learn how to customize your debug container and how Visual Studio uses this Dockerfile to build your images for faster debugging.

# This stage is used when running from VS in fast mode (Default for Debug configuration)
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 80
EXPOSE 443

# Real font files under /usr/share/fonts. The plans and the report's figures are drawn with
# SkiaSharp, whose Linux build here is NativeAssets.Linux.NoDependencies - it scans that
# directory rather than asking fontconfig.
#
# fonts-liberation is not a duplicate of dejavu: Liberation Sans is METRICALLY IDENTICAL to
# Arial, the family the figures are lettered in. The layout measures every label to place it
# and to size its halo, and the Word file asks the reader's Word for Arial; a container
# measuring in DejaVu - whose advances differ by several per cent - would place labels by
# widths nobody ever draws. With no font at all every label measures 0 wide and the PNGs come
# out without a word on them.
# The final stage is FROM base, so what is installed here is what the running image has.
RUN apt-get update && apt-get install -y --no-install-recommends \
    libfontconfig1 \
    fonts-dejavu-core \
    fonts-liberation \
    && rm -rf /var/lib/apt/lists/*

# Add debugging toolsr
#RUN apt-get update \
    #&& apt-get install -y --no-install-recommends unzip curl \
    #&& curl -sSL https://aka.ms/getvsdbgsh | bash /dev/stdin -v latest -l /root/.vsdbg

    # Add Node.js installation if needed
#RUN apt-get update && apt-get install -y curl && \
    #curl -fsSL https://deb.nodesource.com/setup_20.x | bash - && \
    #apt-get install -y nodejs


    ## Optional: Check node and npm versions during build
#RUN node -v && npm -v

# This stage is used to build the service project. SDK 10, the one Visual Studio builds with, so the image
# compiles the Razor pages exactly as Visual Studio does (the SDK 8 Razor parser rejected code VS accepted).
# It still builds a .NET 8 app (net8.0) for the aspnet:8.0 runtime above.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["AliantWebS/Images", "/app/Images"]
COPY ["AliantWebS/AliantWebS.csproj", "AliantWebS/"]
COPY ["Structural/Structural.csproj", "Structural/"]
COPY ["Shared/Shared.csproj", "Shared/"]
COPY ["Component/Components.csproj", "Component/"]
#COPY ["Reporting/Reporting.csproj", "Reporting/"]
RUN dotnet restore "./AliantWebS/AliantWebS.csproj"
COPY . .
WORKDIR "/src/AliantWebS"
RUN dotnet build "./AliantWebS.csproj" -c $BUILD_CONFIGURATION -o /app/build

# This stage is used to publish the service project to be copied to the final stage
FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./AliantWebS.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

# This stage is used in production or when running from VS in regular mode (Default when not using the Debug configuration)
FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
RUN find . -name "*.pdb" -exec echo "Including symbol file: {}" \;
ENTRYPOINT ["dotnet", "AliantWebS.dll"]