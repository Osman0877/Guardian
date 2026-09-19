FROM mcr.microsoft.com/dotnet/sdk:10.0-preview AS build
WORKDIR /src

COPY ["Guardian.Core/Guardian.Core.csproj", "Guardian.Core/"]
COPY ["Guardian.Server/Guardian.Server.csproj", "Guardian.Server/"]
RUN dotnet restore "Guardian.Server/Guardian.Server.csproj"

COPY . .
WORKDIR "/src/Guardian.Server"
RUN dotnet publish "Guardian.Server.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0-preview AS final
WORKDIR /app
COPY --from=build /app/publish .
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "Guardian.Server.dll"]
