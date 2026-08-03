FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

COPY MedivalChess.Shared/MedivalChess.Shared.csproj MedivalChess.Shared/
COPY MedivalChess.Server/MedivalChess.Server.csproj MedivalChess.Server/
RUN dotnet restore MedivalChess.Server/MedivalChess.Server.csproj

COPY MedivalChess.Shared/ MedivalChess.Shared/
COPY MedivalChess.Server/ MedivalChess.Server/
# The shared rules project links Board and terrain sources plus board JSON assets from this folder.
COPY GameBoard/ GameBoard/
# The shared rules project also links this root-level defaults file.
COPY Globals.cs ./
RUN dotnet publish MedivalChess.Server/MedivalChess.Server.csproj --configuration Release --no-restore --output /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "MedivalChess.Server.dll"]
