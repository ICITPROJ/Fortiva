using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;

namespace Fortiva.AppHost.Services;

/// <summary>Subtle elevation, hover, and surface treatments for premium UI finish.</summary>
public static class FortivaSurfaceEffects
{
    public static void ApplyCardElevation(UIElement element, float depth = 8f)
    {
        element.Shadow = Application.Current?.Resources["FortivaCardShadow"] as ThemeShadow ?? new ThemeShadow();
        element.Translation = new System.Numerics.Vector3(0, 0, depth);
    }

    public static void ApplyDialogElevation(UIElement element)
    {
        element.Shadow = Application.Current?.Resources["FortivaDialogShadow"] as ThemeShadow ?? new ThemeShadow();
        element.Translation = new System.Numerics.Vector3(0, 0, 24);
    }

    public static void ApplyHoverLift(Border border, float resting = 6f, float hover = 14f)
    {
        ApplyCardElevation(border, resting);
        border.PointerEntered += (_, _) => border.Translation = new System.Numerics.Vector3(0, -1, hover);
        border.PointerExited += (_, _) => border.Translation = new System.Numerics.Vector3(0, 0, resting);
    }

    public static void ApplyChipToggle(ToggleButton toggle, bool selected, FrameworkElement context, ElementTheme? theme = null)
        => FortivaControlTheme.ApplyCategoryChip(toggle, selected, context, theme);

    public static void ApplyIconButton(Button button, FrameworkElement? context = null)
    {
        FortivaControlTheme.TryApplyStyle(button, "FortivaIconButton");
        var theme = FortivaControlTheme.ResolveEffectiveTheme(context?.XamlRoot, context ?? button);
        button.Foreground = FortivaControlTheme.GetBrush("FortivaMutedBrush", theme, context ?? button);
    }

    /// <summary>Brief scale pulse when an action succeeds (copy, save).</summary>
    public static void PulseSuccess(FrameworkElement element)
    {
        try
        {
            var visual = ElementCompositionPreview.GetElementVisual(element);
            var compositor = visual.Compositor;
            var scale = compositor.CreateVector3KeyFrameAnimation();
            scale.InsertKeyFrame(0f, new System.Numerics.Vector3(1f, 1f, 1f));
            scale.InsertKeyFrame(0.35f, new System.Numerics.Vector3(1.02f, 1.02f, 1f));
            scale.InsertKeyFrame(1f, new System.Numerics.Vector3(1f, 1f, 1f));
            scale.Duration = TimeSpan.FromMilliseconds(280);
            visual.CenterPoint = new System.Numerics.Vector3(
                (float)(element.ActualWidth / 2),
                (float)(element.ActualHeight / 2),
                0);
            visual.StartAnimation("Scale", scale);
        }
        catch
        {
            /* composition optional */
        }
    }
}
