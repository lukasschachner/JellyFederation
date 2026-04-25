using JellyFederation.Server.Services;
using JellyFederation.Shared.Models;
using Microsoft.AspNetCore.Mvc;

namespace JellyFederation.Server.Pagination;

internal readonly record struct PageRequest(int Page, int PageSize)
{
    public const int DefaultPage = 1;
    public const int DefaultPageSize = 100;
    public const int MaxPageSize = 500;

    public int Skip => (Page - 1) * PageSize;
}

internal static class PaginationHeaders
{
    public static ObjectResult? Validate(int page, int pageSize, string code, string correlationId)
    {
        if (page >= 1 && pageSize is >= 1 and <= PageRequest.MaxPageSize)
            return null;

        return ErrorContractMapper.ToActionResult(FailureDescriptor.Validation(
            code,
            $"Invalid pagination parameters. page must be >= 1 and pageSize must be between 1 and {PageRequest.MaxPageSize}.",
            correlationId));
    }

    public static void Add(HttpResponse response, PageRequest pageRequest, int total)
    {
        response.Headers["X-Total-Count"] = total.ToString();
        response.Headers["X-Page"] = pageRequest.Page.ToString();
        response.Headers["X-Page-Size"] = pageRequest.PageSize.ToString();
        response.Headers["X-Total-Pages"] = ((total + pageRequest.PageSize - 1) / pageRequest.PageSize).ToString();
    }
}
