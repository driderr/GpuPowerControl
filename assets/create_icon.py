from PIL import Image, ImageDraw, ImageFont

def create_icon():
    size = 512
    canvas = Image.new('RGBA', (size, size), (0, 0, 0, 0))
    draw = ImageDraw.Draw(canvas)

    # === GPU CHIP BACKGROUND ===
    # Draw a stylized GPU chip - rectangle with rounded corners
    margin = 60
    chip_color = (30, 136, 229)  # Blue
    draw.rounded_rectangle(
        [margin, margin, size - margin, size - margin],
        radius=40,
        fill=chip_color
    )

    # GPU pins (top and bottom)
    pin_color = (200, 200, 200)  # Light gray
    num_pins = 8
    pin_width = 15
    pin_spacing = 35
    start_x = 100
    for i in range(num_pins):
        x = start_x + i * pin_spacing
        # Top pins
        draw.rectangle([x, margin - 15, x + pin_width, margin], fill=pin_color)
        # Bottom pins
        draw.rectangle([x, size - margin, x + pin_width, size - margin + 15], fill=pin_color)

    # Pin 1 indicator: small gray circle in top-left corner
    pin1_color = (150, 150, 150)
    draw.ellipse(
        [margin + 15, margin + 15, margin + 35, margin + 35],
        fill=pin1_color
    )

    # Chip text markings (like "GTX 4090" or model number)
    text_color = (255, 255, 255)
    text_x = size // 2
    text_y = int(size * 0.5) - 18
    # Use Windows Arial font
    font_path = r"C:\Windows\Fonts\arial.ttf"
    font_large = ImageFont.truetype(font_path, 28)
    font_medium = ImageFont.truetype(font_path, 24)
    draw.text(
        (text_x, text_y), "GTX", fill=text_color, anchor="md", font=font_large
    )
    draw.text(
        (text_x, text_y + 35), "4090", fill=text_color, anchor="md", font=font_medium
    )

    # === THERMOMETER OVERLAY ===
    thermo_x = int(size * 0.55)
    thermo_y = int(size * 0.2)
    thermo_w = int(size * 0.25)
    thermo_h = int(size * 0.5)

    # Thermometer glass tube (outline)
    tube_color = (100, 100, 100)  # Dark gray outline
    tube_thickness = 8

    # Thermometer body (rectangular tube)
    tube_x1 = thermo_x
    tube_x2 = thermo_x + thermo_w
    tube_y1 = thermo_y
    tube_y2 = thermo_y + thermo_h - int(thermo_h * 0.25)
    draw.rectangle(
        [tube_x1, tube_y1, tube_x2, tube_y2],
        fill=None,
        outline=tube_color,
        width=tube_thickness
    )

    # Thermometer bulb (circle at bottom)
    bulb_cx = thermo_x + thermo_w // 2
    bulb_cy = thermo_y + thermo_h - int(thermo_h * 0.25) + int(thermo_w * 0.2)
    bulb_r = int(thermo_w * 0.6)
    draw.ellipse(
        [bulb_cx - bulb_r, bulb_cy - bulb_r, bulb_cx + bulb_r, bulb_cy + bulb_r],
        fill=None,
        outline=tube_color,
        width=tube_thickness
    )

    # Red mercury fill in bulb
    mercury_color = (220, 40, 40)  # Red
    draw.ellipse(
        [bulb_cx - bulb_r + 4, bulb_cy - bulb_r + 4, bulb_cx + bulb_r - 4, bulb_cy + bulb_r - 4],
        fill=mercury_color
    )

    # Warning symbol (triangle with exclamation) centered in bulb
    warn_size = int(bulb_r * 1.2)
    half_w = int(warn_size * 0.5)
    tri_top = bulb_cy - warn_size * 0.4
    tri_bottom = bulb_cy + warn_size * 0.5
    # Triangle centroid: (top_y + 2*bottom_y) / 3
    tri_cx = bulb_cx
    tri_cy = int((tri_top + 2 * tri_bottom) / 3)
    triangle = [
        (bulb_cx, tri_top),
        (bulb_cx - half_w, tri_bottom),
        (bulb_cx + half_w, tri_bottom)
    ]
    warning_color = (255, 255, 100)  # Yellow for warning
    draw.polygon(triangle, fill=warning_color)

    # Exclamation mark - use absolute Y offset from triangle bottom
    font_path = r"C:\Windows\Fonts\arial.ttf"
    font_excl = ImageFont.truetype(font_path, int(warn_size * 0.5))
    # Place the exclamation near the bottom of the triangle
    excl_y = int(tri_bottom - warn_size * 0.15)
    draw.text(
        (tri_cx, excl_y), "!", fill=(220, 40, 40), anchor="md", font=font_excl
    )

    # Measurement lines on the right side of the tube (outside)
    tube_h = tube_y2 - tube_y1
    num_marks = 8
    mark_color = (100, 100, 100)
    for i in range(num_marks):
        y = tube_y1 + int((i + 1) * tube_h / (num_marks + 1))
        # Alternating lengths
        if i % 2 == 0:
            draw.line(
                [(tube_x2, y), (tube_x2 + 18, y)],
                fill=mark_color,
                width=2
            )
        else:
            draw.line(
                [(tube_x2, y), (tube_x2 + 8, y)],
                fill=mark_color,
                width=2
            )

    # Red mercury fill in tube (filling ~70% of the tube height)
    mercury_fill_height = int((tube_y2 - tube_y1) * 0.7)
    draw.rectangle(
        [tube_x1 + tube_thickness, tube_y2 - mercury_fill_height, tube_x2 - tube_thickness, tube_y2],
        fill=mercury_color
    )

    # Save
    canvas.save('assets/gpu-thermo-icon.png', 'PNG')
    print('Icon created: assets/gpu-thermo-icon.png')

    # Generate ICO
    sizes = [(16, 16), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)]
    canvas.save('assets/gpu-thermo-icon.ico', format='ICO', sizes=sizes)
    print('ICO created: assets/gpu-thermo-icon.ico')

create_icon()