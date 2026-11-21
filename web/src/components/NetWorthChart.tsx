import { useEffect, useRef } from "react";
import {
  Chart,
  LineController,
  LineElement,
  PointElement,
  LinearScale,
  CategoryScale,
  Filler,
  Tooltip,
} from "chart.js";
import type { HistoryPoint } from "../api";
import { formatTime } from "../format";

Chart.register(LineController, LineElement, PointElement, LinearScale, CategoryScale, Filler, Tooltip);

interface Props {
  points: HistoryPoint[];
  unit: "divine" | "chaos";
}

/** Smooth area chart of net worth over time, in divines or chaos. */
export function NetWorthChart({ points, unit }: Props) {
  const canvas = useRef<HTMLCanvasElement>(null);
  const chart = useRef<Chart | null>(null);

  useEffect(() => {
    if (!canvas.current) return;
    const values = points.map((p) => (unit === "divine" && p.divineChaos > 0 ? p.totalChaos / p.divineChaos : p.totalChaos));
    const labels = points.map((p) => formatTime(p.ts));
    const ctx = canvas.current.getContext("2d")!;
    const gradient = ctx.createLinearGradient(0, 0, 0, 260);
    gradient.addColorStop(0, "rgba(200, 164, 92, 0.35)");
    gradient.addColorStop(1, "rgba(200, 164, 92, 0.02)");

    chart.current?.destroy();
    chart.current = new Chart(ctx, {
      type: "line",
      data: {
        labels,
        datasets: [
          {
            data: values,
            borderColor: "#d8b66a",
            backgroundColor: gradient,
            fill: true,
            tension: 0.35,
            borderWidth: 2,
            pointRadius: points.length > 120 ? 0 : 2,
            pointHoverRadius: 4,
            pointBackgroundColor: "#d8b66a",
          },
        ],
      },
      options: {
        responsive: true,
        maintainAspectRatio: false,
        animation: false,
        interaction: { mode: "index", intersect: false },
        plugins: {
          legend: { display: false },
          tooltip: {
            callbacks: {
              label: (item) => `${(item.raw as number).toFixed(unit === "divine" ? 2 : 0)} ${unit === "divine" ? "div" : "c"}`,
            },
          },
        },
        scales: {
          x: {
            ticks: { color: "#8c93a1", maxTicksLimit: 7, maxRotation: 0, font: { size: 11 } },
            grid: { color: "rgba(255,255,255,0.05)" },
          },
          y: {
            beginAtZero: true,
            ticks: { color: "#8c93a1", font: { size: 11 } },
            grid: { color: "rgba(255,255,255,0.05)" },
          },
        },
      },
    });
    return () => {
      chart.current?.destroy();
      chart.current = null;
    };
  }, [points, unit]);

  return (
    <div className="chart-box">
      <canvas ref={canvas} />
    </div>
  );
}
