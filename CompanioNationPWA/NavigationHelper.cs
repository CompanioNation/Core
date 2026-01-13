using Microsoft.AspNetCore.Components;


namespace CompanioNationPWA
{

    public class NavigationHelper
    {
        private readonly NavigationManager _navigationManager;

        // Inject the NavigationManager through the constructor
        public NavigationHelper(NavigationManager navigationManager)
        {
            _navigationManager = navigationManager;
        }

        // Non-static method to navigate to the current page
        public void NavigateToCurrentPage()
        {
            // Force Blazor to re-render the current page without a full reload
            _navigationManager.NavigateTo(_navigationManager.Uri, forceLoad: false);
        }
    }
}
