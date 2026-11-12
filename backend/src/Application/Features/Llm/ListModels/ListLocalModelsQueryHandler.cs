using AiPlatform.Application.Common.Interfaces;
using MediatR;

namespace AiPlatform.Application.Features.Llm.ListModels;

public class ListLocalModelsQueryHandler(ILocalModelCatalog catalog)
    : IRequestHandler<ListLocalModelsQuery, IReadOnlyList<string>>
{
    public Task<IReadOnlyList<string>> Handle(ListLocalModelsQuery request, CancellationToken cancellationToken)
        => catalog.ListModelsAsync(request.Provider, cancellationToken);
}
