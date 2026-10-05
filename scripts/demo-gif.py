"""Arma docs/img/demo.gif con las capturas que guardan las pruebas e2e (HELPDESK_E2E_SCREENSHOTS=docs/img).

Uso: python scripts/demo-gif.py [carpeta]   (requiere Pillow)
"""

import sys
from pathlib import Path

from PIL import Image, ImageDraw, ImageFont

FRAMES = [
    ("tablero.png", "Supervisión: SLA en riesgo e incumplidos, calculados en horario hábil de Ecuador"),
    ("flujo-1.png", "El cliente registra su solicitud; la prioridad sale de la matriz ITIL"),
    ("flujo-2.png", "Sin recargar: el ticket aparece en la bandeja de la supervisora (SignalR)"),
    ("flujo-3.png", "La supervisora lo asigna a un agente de la cola"),
    ("flujo-4.png", "El cliente ve en vivo la respuesta pública del agente"),
    ("flujo-5.png", "El agente lo resuelve con un resumen de la solución"),
    ("ticket-detalle-cliente.png", "El cliente confirma o reabre y califica la atención (CSAT)"),
    ("sugerencia-ia.png", "Sugerencia del clasificador nlp-tickets-es: un agente la acepta o la corrige"),
    ("reportes.png", "Reportes: tiempos de respuesta y resolución, cumplimiento de SLA y carga por agente"),
]
WIDTH = 960
CAPTION_HEIGHT = 44
FRAME_MS = 2600


def caption_font(size: int) -> ImageFont.ImageFont:
    for name in ("segoeui.ttf", "DejaVuSans.ttf", "Arial.ttf"):
        try:
            return ImageFont.truetype(name, size)
        except OSError:
            continue
    return ImageFont.load_default()


def main() -> int:
    folder = Path(sys.argv[1] if len(sys.argv) > 1 else "docs/img")
    font = caption_font(19)
    frames = []
    for file_name, text in FRAMES:
        path = folder / file_name
        if not path.exists():
            print(f"Falta {path}: corre antes las pruebas e2e con HELPDESK_E2E_SCREENSHOTS={folder}")
            return 1
        shot = Image.open(path).convert("RGB")
        shot = shot.resize((WIDTH, round(shot.height * WIDTH / shot.width)), Image.Resampling.LANCZOS)
        frame = Image.new("RGB", (WIDTH, shot.height + CAPTION_HEIGHT), (15, 92, 110))
        frame.paste(shot, (0, CAPTION_HEIGHT))
        ImageDraw.Draw(frame).text((16, 11), text, fill=(255, 255, 255), font=font)
        frames.append(frame.quantize(colors=128, method=Image.Quantize.MEDIANCUT))

    output = folder / "demo.gif"
    frames[0].save(output, save_all=True, append_images=frames[1:], duration=FRAME_MS, loop=0, optimize=True)
    print(f"{output} ({output.stat().st_size / 1024:.0f} KB, {len(frames)} cuadros)")
    return 0


if __name__ == "__main__":
    sys.exit(main())
