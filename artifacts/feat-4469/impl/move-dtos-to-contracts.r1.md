# move-dtos-to-contracts r1
Moved StockAnalysisItemDto, StockAnalysisSummaryDto, LastPurchaseInfoDto verbatim into Purchase/Contracts (one file each, Contracts namespace). Removed from GetPurchaseStockAnalysisResponse.cs. No other file needed a new using. `dotnet build Anela.Heblo.sln`: 0 errors.
