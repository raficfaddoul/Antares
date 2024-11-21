# See https://aka.ms/customizecontainer to learn how to customize your debug container and how Visual Studio uses this Dockerfile to build your images for faster debugging.

# This stage is used when running from VS in fast mode (Default for Debug configuration)
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 80
EXPOSE 443

# Add debugging toolsr
#RUN apt-get update \
    #&& apt-get install -y --no-install-recommends unzip curl \
    #&& curl -sSL https://aka.ms/getvsdbgsh | bash /dev/stdin -v latest -l /root/.vsdbg

    # Add Node.js installation if needed
#RUN apt-get update && apt-get install -y curl && \
    #curl -fsSL https://deb.nodesource.com/setup_16.x | bash - && \
    #apt-get install -y nodejs && \
    #npm install -g npm

# This stage is used to build the service project
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["AliantWebS/AliantWebS.csproj", "AliantWebS/"]
COPY ["AI/AI.csproj", "AI/"]
COPY ["Structural/Structural.csproj", "Structural/"]
COPY ["Shared/Shared.csproj", "Shared/"]
COPY ["Component/Components.csproj", "Component/"]
COPY ["Reporting/Reporting.csproj", "Reporting/"]
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