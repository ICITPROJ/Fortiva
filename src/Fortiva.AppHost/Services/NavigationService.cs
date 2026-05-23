using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;

namespace Fortiva.AppHost.Services;

public sealed class NavigationService
{
    private Frame? _frame;
    private Type? _currentPageType;
    private object? _currentParameter;

    public static NavigationService Current { get; } = new();

    public void Initialize(Frame frame) => _frame = frame;

    public Type? CurrentPageType => _currentPageType;

    /// <summary>
    /// Navigate to a page. Tab switches use <paramref name="animate"/> = false (no transition lag).
    /// </summary>
    public bool Navigate<TPage>(object? parameter = null, bool animate = false)
        where TPage : Microsoft.UI.Xaml.Controls.Page
        => Navigate(typeof(TPage), parameter, animate);

    public bool Navigate(Type pageType, object? parameter = null, bool animate = false)
    {
        if (_frame is null) return false;

        // Skip redundant navigation — avoids re-running OnNavigatedTo and transition jank
        if (_currentPageType == pageType && ParametersEqual(_currentParameter, parameter))
            return true;

        var transition = animate
            ? (NavigationTransitionInfo)new EntranceNavigationTransitionInfo()
            : new SuppressNavigationTransitionInfo();

        if (_frame.Navigate(pageType, parameter, transition) != true)
            return false;

        _currentPageType = pageType;
        _currentParameter = parameter;
        return true;
    }

    public bool GoBack()
    {
        if (_frame?.CanGoBack != true) return false;
        _frame.GoBack();
        _currentPageType = _frame.CurrentSourcePageType as Type;
        _currentParameter = null;
        return true;
    }

    public bool CanGoBack => _frame?.CanGoBack ?? false;

    public void ClearHistory() => _frame?.BackStack.Clear();

    public void ResetCurrent()
    {
        _currentPageType = null;
        _currentParameter = null;
    }

    private static bool ParametersEqual(object? a, object? b)
    {
        if (a is null && b is null) return true;
        if (a is null || b is null) return false;
        return a.Equals(b);
    }
}
