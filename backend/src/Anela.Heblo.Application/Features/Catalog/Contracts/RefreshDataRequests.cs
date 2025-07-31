using MediatR;

namespace Anela.Heblo.Application.Features.Catalog.Contracts;

public record RefreshTransportDataRequest() : IRequest;
public record RefreshReserveDataRequest() : IRequest;
public record RefreshSalesDataRequest() : IRequest;
public record RefreshAttributesDataRequest() : IRequest;
public record RefreshErpStockDataRequest() : IRequest;
public record RefreshEshopStockDataRequest() : IRequest;
public record RefreshPurchaseHistoryDataRequest() : IRequest;
public record RefreshConsumedHistoryDataRequest() : IRequest;
public record RefreshStockTakingDataRequest() : IRequest;
public record RefreshLotsDataRequest() : IRequest;