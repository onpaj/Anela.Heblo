using MediatR;

namespace Anela.Heblo.Application.Features.Catalog.Model;

public record RefreshTransportDataRequest() : IRequest;
public record RefreshReserveDataRequest() : IRequest;
public record RefreshSalesDataRequest() : IRequest;
public record RefreshAttributesDataRequest() : IRequest;
public record RefreshErpStockDataRequest() : IRequest;
public record RefreshEshopStockDataRequest() : IRequest;
public record RefreshPurchaseHistoryDataRequest() : IRequest;
public record RefreshManufactureHistoryDataRequest() : IRequest;
public record RefreshConsumedHistoryDataRequest() : IRequest;
public record RefreshStockTakingDataRequest() : IRequest;
public record RefreshLotsDataRequest() : IRequest;
public record RefreshEshopPricesDataRequest() : IRequest;
public record RefreshErpPricesDataRequest() : IRequest;
public record RefreshManufactureDifficultyDataRequest() : IRequest;
public record RefreshManufactureCostDataRequest() : IRequest;