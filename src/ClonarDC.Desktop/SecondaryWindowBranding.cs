namespace ClonarDC;

public partial class RegisterWindow
{
    protected override void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);
        RegisterBrandHost.Content ??= BrandPresentation.CreateMark(62, glow: false);
        RegisterSuccessBrandHost.Content ??= BrandPresentation.CreateMark(104);
    }
}

public partial class PublishUpdateWindow
{
    protected override void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);
        PublishBrandHost.Content ??= BrandPresentation.CreateMark(64, glow: false);
    }
}

public partial class TextConfirmWindow
{
    protected override void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);
        ConfirmBrandHost.Content ??= BrandPresentation.CreateMark(49, glow: false);
    }
}
