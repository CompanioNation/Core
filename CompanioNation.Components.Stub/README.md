# CompanioNation.Components

Open-source stub implementation of CompanioNation UI components for Blazor WebAssembly.

## What's Included

- **SubscribeToCompanioNita** - Subscription dialog component (stub/placeholder version)

## Installation

```bash
dotnet add package CompanioNation.Components
```

## Usage

```razor
@using CompanioNation.Components

<SubscribeToCompanioNita @ref="subscribeComponent" 
                         OnSubscribeFinished="HandleSubscribeFinished" />

@code {
    private SubscribeToCompanioNita subscribeComponent;

    private async Task ShowSubscribe()
    {
        await subscribeComponent.ShowSubscribe();
    }

    private void HandleSubscribeFinished()
    {
        // Handle completion
    }
}
```

## Notes

This is the **open-source stub version**. The full implementation with payment processing is available separately for production deployments.

## Repository

[CompanioNation/Core](https://github.com/CompanioNation/Core)

## License

Licensed under the CompanioNation Public Licence (CPL-1.0). See LICENSE in the repository.
