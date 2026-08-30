// Chart.js interop for the admin statistics view.
// Loaded as a collocated ES module by Charts.razor. Requires the self-hosted
// Chart.js UMD bundle (wwwroot/js/vendor/chart.umd.min.js) to be present on
// the page so window.Chart is defined before the first render call.
const chartRegistry = new Map();

function getChartLib() {
    if (!window.Chart) {
        console.error('Charts.razor.js: Chart.js is not loaded. Add js/vendor/chart.umd.min.js to the page shell.');
        return null;
    }
    return window.Chart;
}

/**
 * Renders (or replaces) a chart on the given canvas element.
 * @param {HTMLCanvasElement} element
 * @param {string} chartId unique id used to track/replace the instance
 * @param {string} type 'line', 'bar', etc.
 * @param {string[]} labels
 * @param {number[]} values
 * @param {string|null} borderColor
 * @param {string|null} backgroundColor
 * @param {boolean} fill
 */
export function renderChart(element, chartId, type, labels, values, borderColor, backgroundColor, fill) {
    destroyChart(chartId);

    const Chart = getChartLib();
    if (!Chart || !element) {
        return null;
    }

    const isLine = type === 'line';
    const chart = new Chart(element.getContext('2d'), {
        type: type || 'line',
        data: {
            labels: labels || [],
            datasets: [{
                label: null,
                data: values || [],
                borderColor: borderColor || '#2c6cb0',
                backgroundColor: backgroundColor || 'rgba(44, 108, 176, 0.12)',
                borderWidth: 2,
                fill: fill !== undefined ? !!fill : isLine,
                tension: isLine ? 0.35 : undefined,
                pointRadius: (values && values.length > 40) ? 0 : 2,
                pointHoverRadius: 4
            }]
        },
        options: {
            responsive: true,
            maintainAspectRatio: false,
            animation: { duration: 250 },
            plugins: {
                legend: { display: false },
                tooltip: {
                    mode: 'index',
                    intersect: false
                }
            },
            scales: {
                x: {
                    ticks: { maxTicksLimit: 12, maxRotation: 45, minRotation: 0, autoSkip: true },
                    grid: { display: false }
                },
                y: {
                    beginAtZero: true,
                    ticks: { precision: 0 }
                }
            }
        }
    });

    chartRegistry.set(chartId, chart);
    return chart;
}

export function destroyChart(chartId) {
    const chart = chartRegistry.get(chartId);
    if (chart) {
        chart.destroy();
        chartRegistry.delete(chartId);
    }
}

export function destroyAllCharts() {
    chartRegistry.forEach((chart) => chart.destroy());
    chartRegistry.clear();
}
