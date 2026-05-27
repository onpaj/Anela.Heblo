namespace Anela.Heblo.Application.Features.Manufacture.UseCases.GetSemiproductRecipePdf;

public interface ISemiproductRecipeRenderer
{
    byte[] Render(SemiproductRecipeData data);
}
