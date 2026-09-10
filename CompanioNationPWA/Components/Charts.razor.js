// Chart.js interop for the admin statistics view.
// Loaded as a collocated ES module by Charts.razor. The self-hosted Chart.js
// UMD bundle (wwwroot/js/vendor/chart.umd.min.js) is fetched on demand the
// first time a chart actually renders, so pages without charts never pay its
// ~200 KB download/parse cost on the critical path.
const chartRegistry = new Map();
const CHART_JS_URL = 'js/vendor/chart.umd.min.js';
let chartLoadPromise = null;

function loadChartLib() {
    if (window.Chart) {
        return Promise.resolve(window.Chart);
    }
    if (!chartLoadPromise) {
        chartLoadPromise = new Promise((resolve, reject) => {
            const script = document.createElement('script');
            script.src = CHART_JS_URL;
            script.onload = () => window.Chart
                ? resolve(window.Chart)
                : reject(new Error('Chart.js loaded but window.Chart is undefined.'));
            script.onerror = () => {
                chartLoadPromise = null; // allow a retry on the next render
                reject(new Error('Failed to load ' + CHART_JS_URL));
            };
            document.head.appendChild(script);
        });
    }
    return chartLoadPromise;
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
export async function renderChart(element, chartId, type, labels, values, borderColor, backgroundColor, fill) {
    destroyChart(chartId);

    if (!element) {
        return null;
    }

    const Chart = await loadChartLib();
    if (!Chart) {
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
