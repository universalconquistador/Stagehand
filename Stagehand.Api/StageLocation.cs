using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using Lumina.Excel.Sheets;
using System;
using System.Collections.Generic;
using System.Text;

namespace Stagehand.Api;

/// <summary>
/// Identifies a distinct place that stages can be assigned to.
/// </summary>
/// <remarks>
/// This includes the world, the territory, the housing ward &amp; division, and the house &amp; room. This does <em>not</em> include the instance for instanced zones.
/// </remarks>
/// <param name="WorldId">The world row ID as found in the <see cref="Lumina.Excel.Sheets.World"/> sheet.</param>
/// <param name="TerritoryId">The territory row ID as found in in the <see cref="Lumina.Excel.Sheets.TerritoryType"/> sheet.</param>
/// <param name="WardId">The ward number from 1-30, or -1 if not in a housing ward or if in a company workshop.</param>
/// <param name="DivisionId">The division number from 1-2, or -1 if not in a housing ward or if in a company workshop.</param>
/// <param name="HouseId">The house number in the division the player is in from 1-30, or 0 for the apartment building, or -1 if not in a housing building or if in a company workshop.</param>
/// <param name="RoomId">The room number (apartment unit/private chambers) the player is in starting at 1, or 0 if the player is in the main house room or apartment lobby, or -1 if not in a housing building or if in a company workshop.</param>
public record struct StageLocation(uint WorldId, ushort TerritoryId, int WardId, int DivisionId, int HouseId, int RoomId)
{
    /// <summary>
    /// Gets the current location of the player, if they are logged in to the game.
    /// </summary>
    /// <param name="clientState">The client state of the game.</param>
    /// <param name="playerState">The player state.</param>
    /// <param name="location">The current location, if any.</param>
    /// <returns>True if the current location could be determined, or false otherwise (e.g. if the player is not logged in).</returns>
    public static unsafe bool TryGetLocation(IClientState clientState, IPlayerState playerState, out StageLocation location)
    {
        if (clientState.IsLoggedIn)
        {
            location = new StageLocation();
            location.TerritoryId = (ushort)clientState.TerritoryType;
            location.WorldId = playerState.CurrentWorld.RowId;

            var housingManager = HousingManager.Instance();
            if (housingManager != null)
            {
                location.WardId = (housingManager->GetCurrentWard() + 1);

                if (housingManager->IsInside())
                {
                    if (housingManager->GetCurrentHouseId().Unit.IsApartment)
                    {
                        // GetCurrentDivision returns 0 indoors. Luckily we can tell by house number.
                        location.DivisionId = (ushort)(housingManager->GetCurrentHouseId().Unit.ApartmentDivision + 1);

                        location.HouseId = 0; // Use zero for the apartment building
                    }
                    else
                    {
                        // GetCurrentDivision returns 0 indoors. Luckily we can tell by house number.
                        location.DivisionId = (ushort)(housingManager->GetCurrentHouseId().Unit.Value > 30 ? 2 : 1);

                        // Each division should use houseIds 0-30
                        location.HouseId = (housingManager->GetCurrentHouseId().Unit.Value % 30) + 1;
                        location.TerritoryId = (ushort)HousingManager.GetOriginalHouseTerritoryTypeId();
                    }

                    location.RoomId = housingManager->GetCurrentRoom();
                }
                else if (housingManager->IsInWorkshop())
                {
                    location.WardId = -1;
                    location.HouseId = -1;
                    location.RoomId = -1;
                    location.DivisionId = -1;
                }
                else
                {
                    location.DivisionId = (ushort)(housingManager->GetCurrentDivision());

                    location.HouseId = -1;
                    location.RoomId = -1;
                }
            }
            else
            {
                location.WardId = -1;
                location.DivisionId = -1;
                location.HouseId = -1;
                location.RoomId = -1;
            }

            return true;
        }
        else
        {
            location = default;
            return false;
        }
    }

    /// <summary>
    /// Generates a human-readable description of this location without resolving any IDs.
    /// </summary>
    /// <remarks>
    /// To resolve the IDs as well, use the <see cref="ToString(IDataManager)"/> overload.
    /// </remarks>
    /// <returns>A description of this location.</returns>
    public override string ToString()
    {
        return $"World: {WorldId}, Territory: {TerritoryId}, Ward: {(WardId != -1 ? WardId.ToString() : "None")}, Division: {(DivisionId != -1 ? (DivisionId == 1 ? "Main Division" : "Subdivision") : "None")}, House: {(HouseId != -1 ? HouseId.ToString() : "None")}, Room: {(RoomId != -1 ? RoomId.ToString() : "None")}";
    }

    /// <summary>
    /// Generates a human-readable description of this location, resolving World and Territory names
    /// where possible using the given data manager.
    /// </summary>
    /// <param name="dataManager">The data manager to use to resolve world and territory names.</param>
    /// <returns>A description of this location.</returns>
    public readonly string ToString(IDataManager dataManager)
    {
        return $"World: {dataManager.GetExcelSheet<World>().GetRowOrDefault(WorldId)?.Name.ToString() ?? WorldId.ToString()}, Territory: {dataManager.GetExcelSheet<TerritoryType>().GetRowOrDefault(TerritoryId)?.PlaceName.ValueNullable?.Name.ToString() ?? TerritoryId.ToString()}, Ward: {(WardId != -1 ? WardId.ToString() : "None")}, Division: {(DivisionId != -1 ? (DivisionId == 1 ? "Main Division" : "Subdivision") : "None")}, House: {(HouseId != -1 ? HouseId.ToString() : "None")}, Room: {(RoomId != -1 ? RoomId.ToString() : "None")}";
    }
}
