using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using System;
using System.Collections.Generic;
using System.Text;

namespace Stagehand.Utils;

/// <summary>
/// 
/// </summary>
/// <param name="WorldId">The world row ID as found in the <see cref="Lumina.Excel.Sheets.World"/> sheet.</param>
/// <param name="TerritoryId">The territory row ID as found in in the <see cref="Lumina.Excel.Sheets.TerritoryType"/> sheet.</param>
/// <param name="WardId">The ward number from 1-30, or -1 if not in a housing ward or if in a company workshop.</param>
/// <param name="DivisionId">The division number from 1-2, or -1 if not in a housing ward or if in a company workshop.</param>
/// <param name="HouseId">The house number in the division the player is in from 1-30, or 0 for the apartment building, or -1 if not in a housing building or if in a company workshop.</param>
/// <param name="RoomId">The room number (apartment unit/private chambers) the player is in starting at 1, or 0 if the player is in the main house room or apartment lobby, or -1 if not in a housing building or if in a company workshop.</param>
public record struct Location(uint WorldId, ushort TerritoryId, int WardId, int DivisionId, int HouseId, int RoomId)
{
    public static unsafe bool TryGetLocation(IClientState clientState, IPlayerState playerState, out Location location)
    {
        if (clientState.IsLoggedIn)
        {
            location = new Location();
            location.TerritoryId = clientState.TerritoryType;
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
}
