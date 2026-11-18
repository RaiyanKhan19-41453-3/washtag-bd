FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY ["Washtag.Domain/Washtag.Domain.csproj", "Washtag.Domain/"]
COPY ["Washtag.Domain/packages.lock.json", "Washtag.Domain/"]
COPY ["Washtag.Application/Washtag.Application.csproj", "Washtag.Application/"]
COPY ["Washtag.Application/packages.lock.json", "Washtag.Application/"]
COPY ["Washtag.Infrastructure/Washtag.Infrastructure.csproj", "Washtag.Infrastructure/"]
COPY ["Washtag.Infrastructure/packages.lock.json", "Washtag.Infrastructure/"]
COPY ["Washtag.Api/Washtag.Api.csproj", "Washtag.Api/"]
COPY ["Washtag.Api/packages.lock.json", "Washtag.Api/"]
RUN dotnet restore "Washtag.Api/Washtag.Api.csproj" --locked-mode
COPY . .
RUN dotnet publish "Washtag.Api/Washtag.Api.csproj" -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final
WORKDIR /app
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "Washtag.Api.dll"]
