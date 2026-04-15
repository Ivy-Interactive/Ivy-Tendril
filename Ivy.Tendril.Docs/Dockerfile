FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
USER $APP_UID
WORKDIR /app
EXPOSE 5000

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
ARG IVY_PACKAGE_VERSION
WORKDIR /src

# Copy project file and restore (NuGet-based, no project references needed)
COPY ["Ivy.Tendril.Docs.csproj", "./"]
RUN dotnet restore "Ivy.Tendril.Docs.csproj" \
    -p:IvyPackageVersion=$IVY_PACKAGE_VERSION

# Copy the rest of the source code
COPY . .

# Install ivy-docs-cli for markdown transformation (TransformMarkdown MSBuild target).
# The target runs "dotnet tool restore" from three directories above the project file,
# which resolves to / inside this container. Create a tool manifest there so the restore
# succeeds, then install the tool globally so it is on PATH.
RUN mkdir -p /.config && \
    echo '{"version":1,"isRoot":true,"tools":{"ivy.docs.compiler":{"version":"0.1.0","commands":["ivy-docs-cli"]}}}' \
    > /.config/dotnet-tools.json && \
    dotnet tool install --global Ivy.Docs.Compiler || true
ENV PATH="$PATH:/root/.dotnet/tools"

# Build and publish
RUN dotnet publish "Ivy.Tendril.Docs.csproj" \
    -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false \
    /p:IvyPackageVersion=$IVY_PACKAGE_VERSION

FROM base AS final
WORKDIR /app
USER root
RUN apt-get update && apt-get install -y --no-install-recommends \
    libc6 \
    libgcc-s1 \
    && apt-get clean && rm -rf /var/lib/apt/lists/*
USER $APP_UID
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "Ivy.Tendril.Docs.dll"]
