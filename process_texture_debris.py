"""
process_texture_debris.py
Creates heavily damaged debris with scattered/rotated pieces
Supports file name filtering and automatic mask generation
"""

import sys
import os
import glob
import random
from PIL import Image, ImageDraw, ImageFilter, ImageOps
import math

def show_help():
    help_text = """
========================================
Texture Debris Generator with Scatter
========================================

DESCRIPTION:
  Creates damaged ship textures with scattered, rotated pieces.
  Can skip files by name and auto-generates matching masks.
  
USAGE:
  Single File:
    python process_texture_debris.py [options] <input_texture> <output_texture>
  
  Batch Folder:
    python process_texture_debris.py [options] --batch <input_folder> <output_folder>

OPTIONS:
  -d, --damage PERCENT      Damage percentage (20-80, default: 50)
  -s, --seed NUMBER         Random seed for reproducible results
  -v, --verbose             Verbose output
  --batch                   Batch process folder
  --batch-gui               Interactive batch mode
  -r, --recursive           Recursive folder search
  --suffix SUFFIX           Output suffix (default: _debris)
  --skip-words "word1,word2"  Skip files containing these words
  --skip-masks              Skip files with "mask" in name
  --scatter                 Enable scatter mode (rotate/move pieces)
  --scatter-amount N        How much to scatter (1-10, default: 3)
  --min-piece-size N        Minimum piece size in pixels (default: 100)
  --auto-mask               Generate mask automatically (default: ON)
  --no-mask                 Don't generate mask
  --invert-mask             Generate inverted mask (white on transparent)

========================================
REALISTIC BATTLE DAMAGE PRESETS
========================================

Light Battle Damage (Minor Skirmish - 25% damage):
  python process_texture_debris.py -s 12345 -d 25 --scatter --scatter-amount 2 --min-piece-size 150 --skip-masks --invert-mask -v --batch "Assets\\Game Objects\\UI\\Sensors" "Assets\\Game Objects\\UI\\Sensors Debris"

Medium Battle Damage (Standard Combat - RECOMMENDED - 40% damage):
  python process_texture_debris.py -s 12345 -d 40 --scatter --scatter-amount 4 --min-piece-size 100 --skip-masks --invert-mask -v --batch "Assets\\Game Objects\\UI\\Sensors" "Assets\\Game Objects\\UI\\Sensors Debris"

Heavy Battle Damage (Catastrophic - 60% damage):
  python process_texture_debris.py -s 12345 -d 60 --scatter --scatter-amount 6 --min-piece-size 75 --skip-masks --invert-mask -v --batch "Assets\\Game Objects\\UI\\Sensors" "Assets\\Game Objects\\UI\\Sensors Debris"

Extreme Damage (Complete Destruction - 75% damage):
  python process_texture_debris.py -s 12345 -d 75 --scatter --scatter-amount 8 --min-piece-size 50 --skip-masks --invert-mask -v --batch "Assets\\Game Objects\\UI\\Sensors" "Assets\\Game Objects\\UI\\Sensors Debris"

========================================
OTHER EXAMPLES
========================================

Interactive mode (easiest - prompts for paths):
  python process_texture_debris.py -s 12345 -d 40 --scatter --scatter-amount 4 --skip-masks --invert-mask -v --batch-gui

Single file with scattered debris:
  python process_texture_debris.py -d 50 --scatter --scatter-amount 5 --invert-mask -v "Assets\\Textures\\Ship.jpg" "Assets\\Textures\\Ship_debris.png"

Batch without mask generation:
  python process_texture_debris.py -d 40 --scatter --no-mask --skip-masks --batch "Assets\\Textures" "Assets\\Textures\\Debris"

Custom skip words:
  python process_texture_debris.py --skip-words "mask,backup,old" --batch "Assets\\Textures" "Assets\\Textures\\Debris"

========================================
TIPS
========================================

- Paths are relative to your Unity project root folder
- Use double backslashes (\\) in paths on Windows
- Or use forward slashes (/) which work on all platforms
- --invert-mask creates white ship on transparent background (PNG)
- --skip-masks automatically skips files with "mask" in the name
- Same seed (-s) ensures reproducible damage patterns
- Medium damage (40%) recommended for realistic battle debris
- Higher scatter amounts make ships less recognizable

========================================
"""
    print(help_text)
    sys.exit(0)

def create_massive_chunk(center_x, center_y, size):
    points = []
    num_points = random.randint(12, 20)
    
    for i in range(num_points):
        angle = (i / num_points) * 360
        radius = size * random.uniform(0.4, 1.8)
        angle_noise = random.uniform(-20, 20)
        final_angle = angle + angle_noise
        
        px = center_x + int(radius * math.cos(math.radians(final_angle)))
        py = center_y + int(radius * math.sin(math.radians(final_angle)))
        points.append((px, py))
    
    return points

def create_edge_bite(edge_x, edge_y, direction, size):
    points = []
    
    if direction == 'top':
        base_angle = 270
    elif direction == 'bottom':
        base_angle = 90
    elif direction == 'left':
        base_angle = 180
    else:
        base_angle = 0
    
    num_points = random.randint(10, 15)
    for i in range(num_points):
        t = i / (num_points - 1)
        angle = base_angle + (t - 0.5) * 180
        radius = size * random.uniform(0.7, 1.3)
        
        px = edge_x + int(radius * math.cos(math.radians(angle)))
        py = edge_y + int(radius * math.sin(math.radians(angle)))
        points.append((px, py))
    
    return points

def create_split_tear(start_x, start_y, direction_angle, length, width):
    points = []
    segments = random.randint(8, 15)
    
    for i in range(segments):
        t = i / (segments - 1)
        angle_wander = random.uniform(-30, 30)
        current_angle = direction_angle + angle_wander
        
        x = start_x + int(length * t * math.cos(math.radians(current_angle)))
        y = start_y + int(length * t * math.sin(math.radians(current_angle)))
        w = width * random.uniform(0.8, 1.5)
        
        offset_angle = current_angle + 90
        x_off = int(w * math.cos(math.radians(offset_angle)))
        y_off = int(w * math.sin(math.radians(offset_angle)))
        points.append((x + x_off, y + y_off))
    
    for i in range(segments - 1, -1, -1):
        t = i / (segments - 1)
        angle_wander = random.uniform(-30, 30)
        current_angle = direction_angle + angle_wander
        
        x = start_x + int(length * t * math.cos(math.radians(current_angle)))
        y = start_y + int(length * t * math.sin(math.radians(current_angle)))
        w = width * random.uniform(0.8, 1.5)
        
        offset_angle = current_angle - 90
        x_off = int(w * math.cos(math.radians(offset_angle)))
        y_off = int(w * math.sin(math.radians(offset_angle)))
        points.append((x + x_off, y + y_off))
    
    return points

def apply_aggressive_damage(image, damage_percent, seed_value, verbose):
    """Apply heavy damage to create separate pieces"""
    width, height = image.size
    
    if image.mode != 'L':
        gray_image = image.convert('L')
    else:
        gray_image = image.copy()
    
    result = gray_image.copy()
    pixels = result.load()
    
    # Find black pixels
    black_pixels = []
    for y in range(height):
        for x in range(width):
            if pixels[x, y] < 128:
                black_pixels.append((x, y))
    
    if not black_pixels:
        if verbose:
            print("  No black pixels found")
        return result
    
    total_pixels = len(black_pixels)
    if verbose:
        print(f"  Ship pixels: {total_pixels}")
    
    # Find edges
    edge_pixels = []
    for x, y in black_pixels:
        is_edge = False
        for dx, dy in [(-1,0), (1,0), (0,-1), (0,1)]:
            nx, ny = x + dx, y + dy
            if 0 <= nx < width and 0 <= ny < height:
                if pixels[nx, ny] >= 128:
                    is_edge = True
                    break
        if is_edge:
            edge_pixels.append((x, y))
    
    if seed_value is not None:
        random.seed(seed_value)
    
    damage_mask = Image.new('L', (width, height), 0)
    draw = ImageDraw.Draw(damage_mask)
    
    avg_size = int(math.sqrt(total_pixels))
    chunk_size = int(avg_size * 0.3)
    
    # More accurate damage calculation
    target_damage = int(total_pixels * (damage_percent / 100.0))
    avg_chunk_area = (chunk_size ** 2) * math.pi * 0.5
    
    # Scale number of chunks more proportionally to damage
    if damage_percent <= 20:
        num_chunks = max(1, int(target_damage / (avg_chunk_area * 2.0)))
    else:
        num_chunks = max(2, int(target_damage / avg_chunk_area))
    
    num_edge_bites = max(1, int(num_chunks * 0.3))
    num_tears = max(0, int(num_chunks * 0.15))
    
    if verbose:
        print(f"  Creating {num_chunks} chunks, {num_edge_bites} bites, {num_tears} tears")
        print(f"  Target damage: {target_damage} pixels ({damage_percent}%)")
    
    # Large chunks
    for i in range(num_chunks):
        if black_pixels:
            center_x, center_y = random.choice(black_pixels)
        else:
            center_x = width // 2
            center_y = height // 2
        
        # Scale chunk size based on damage percentage
        size_multiplier = 0.8 + (damage_percent / 100.0) * 0.7
        size = int(chunk_size * random.uniform(size_multiplier * 0.9, size_multiplier * 1.1))
        points = create_massive_chunk(center_x, center_y, size)
        
        if len(points) >= 3:
            draw.polygon(points, fill=255)
    
    # Edge bites
    if seed_value is not None:
        random.seed(seed_value + 1000)
    
    for i in range(num_edge_bites):
        if edge_pixels:
            edge_x, edge_y = random.choice(edge_pixels)
        else:
            edge_x = random.choice([0, width-1])
            edge_y = random.randint(0, height-1)
        
        if edge_x < width * 0.3:
            direction = 'left'
        elif edge_x > width * 0.7:
            direction = 'right'
        elif edge_y < height * 0.3:
            direction = 'top'
        else:
            direction = 'bottom'
        
        size = int(chunk_size * random.uniform(0.5, 1.0))
        points = create_edge_bite(edge_x, edge_y, direction, size)
        
        if len(points) >= 3:
            draw.polygon(points, fill=255)
    
    # Tears
    if seed_value is not None:
        random.seed(seed_value + 2000)
    
    for i in range(num_tears):
        if black_pixels:
            start_x, start_y = random.choice(black_pixels)
        else:
            start_x = width // 2
            start_y = height // 2
        
        tear_angle = random.uniform(0, 360)
        tear_length = int(avg_size * random.uniform(0.4, 0.8))
        tear_width = int(chunk_size * random.uniform(0.15, 0.3))
        
        points = create_split_tear(start_x, start_y, tear_angle, tear_length, tear_width)
        
        if len(points) >= 3:
            draw.polygon(points, fill=255)
    
    # Apply damage
    mask_pixels = damage_mask.load()
    result_pixels = result.load()
    
    for y in range(height):
        for x in range(width):
            if mask_pixels[x, y] > 128:
                result_pixels[x, y] = 255
    
    # Reduce erosion for lower damage levels
    if damage_percent <= 20:
        erosion_passes = max(1, int(damage_percent / 25))
    else:
        erosion_passes = max(2, int(damage_percent / 20))
    
    if verbose:
        print(f"  Erosion passes: {erosion_passes}")
    
    for _ in range(erosion_passes):
        result = result.filter(ImageFilter.MaxFilter(5))
    
    # Reduce edge noise for lower damage
    if seed_value is not None:
        random.seed(seed_value + 3000)
    
    result_pixels = result.load()
    noise_multiplier = max(0.005, damage_percent / 100.0 * 0.02)
    noise_count = int(total_pixels * noise_multiplier)
    
    for _ in range(noise_count):
        if black_pixels:
            x, y = random.choice(black_pixels)
            if random.random() < 0.6:
                result_pixels[x, y] = 255
    
    return result

def find_connected_pieces(image, min_size, verbose):
    """Find separate connected pieces using flood fill"""
    width, height = image.size
    pixels = image.load()
    
    visited = [[False] * width for _ in range(height)]
    pieces = []
    
    def flood_fill(start_x, start_y):
        """Flood fill to find connected black pixels"""
        stack = [(start_x, start_y)]
        piece_pixels = []
        
        while stack:
            x, y = stack.pop()
            
            if x < 0 or x >= width or y < 0 or y >= height:
                continue
            if visited[y][x]:
                continue
            if pixels[x, y] >= 128:  # White pixel
                continue
            
            visited[y][x] = True
            piece_pixels.append((x, y))
            
            # Check 4 neighbors
            stack.append((x + 1, y))
            stack.append((x - 1, y))
            stack.append((x, y + 1))
            stack.append((x, y - 1))
        
        return piece_pixels
    
    # Find all pieces
    for y in range(height):
        for x in range(width):
            if pixels[x, y] < 128 and not visited[y][x]:
                piece_pixels = flood_fill(x, y)
                
                if len(piece_pixels) >= min_size:
                    pieces.append(piece_pixels)
    
    if verbose:
        print(f"  Found {len(pieces)} pieces (min size: {min_size}px)")
    
    return pieces

def scatter_pieces(image, pieces, scatter_amount, seed_value, verbose):
    """Rotate and move pieces around"""
    width, height = image.size
    
    if seed_value is not None:
        random.seed(seed_value + 5000)
    
    # Create new blank canvas
    result = Image.new('L', (width, height), 255)
    result_pixels = result.load()
    
    if verbose:
        print(f"  Scattering {len(pieces)} pieces...")
    
    for i, piece_pixels in enumerate(pieces):
        if not piece_pixels:
            continue
        
        # Find bounding box
        xs = [p[0] for p in piece_pixels]
        ys = [p[1] for p in piece_pixels]
        min_x, max_x = min(xs), max(xs)
        min_y, max_y = min(ys), max(ys)
        
        piece_width = max_x - min_x + 1
        piece_height = max_y - min_y + 1
        
        # Create piece image
        piece_img = Image.new('L', (piece_width, piece_height), 255)
        piece_pixels_obj = piece_img.load()
        
        for x, y in piece_pixels:
            local_x = x - min_x
            local_y = y - min_y
            piece_pixels_obj[local_x, local_y] = 0  # Black
        
        # Random rotation
        rotation = random.uniform(-180, 180)
        rotated = piece_img.rotate(rotation, expand=True, fillcolor=255)
        rotated_pixels = rotated.load()
        
        # Random offset based on scatter amount
        max_offset = int((width + height) / 8 * (scatter_amount / 10.0))
        offset_x = random.randint(-max_offset, max_offset)
        offset_y = random.randint(-max_offset, max_offset)
        
        # Calculate center position
        center_x = (min_x + max_x) // 2
        center_y = (min_y + max_y) // 2
        
        # New position
        paste_x = center_x + offset_x - rotated.width // 2
        paste_y = center_y + offset_y - rotated.height // 2
        
        # Manually copy black pixels from rotated piece to result
        for py in range(rotated.height):
            for px in range(rotated.width):
                if rotated_pixels[px, py] < 128:  # Black pixel
                    dest_x = paste_x + px
                    dest_y = paste_y + py
                    if 0 <= dest_x < width and 0 <= dest_y < height:
                        result_pixels[dest_x, dest_y] = 0  # Black
    
    if verbose:
        print(f"  Scattered with rotation and movement")
    
    return result

def generate_mask_from_damaged(damaged_image, invert, verbose):
    """Generate mask from damaged texture"""
    width, height = damaged_image.size
    
    if damaged_image.mode != 'L':
        gray = damaged_image.convert('L')
    else:
        gray = damaged_image.copy()
    
    if invert:
        # Create RGBA mask with transparency for inverted mode
        mask = Image.new('RGBA', (width, height), (0, 0, 0, 0))  # Transparent background
        
        gray_pixels = gray.load()
        mask_pixels = mask.load()
        
        for y in range(height):
            for x in range(width):
                if gray_pixels[x, y] < 128:  # Black pixel (ship part)
                    # White ship on transparent background
                    mask_pixels[x, y] = (255, 255, 255, 255)
                # else: leave transparent (0, 0, 0, 0)
        
        if verbose:
            print(f"  Generated inverted mask (white on transparent)")
    else:
        # Create regular mask (black on white)
        mask = Image.new('L', (width, height), 255)
        
        gray_pixels = gray.load()
        mask_pixels = mask.load()
        
        for y in range(height):
            for x in range(width):
                if gray_pixels[x, y] < 128:
                    mask_pixels[x, y] = 0
                else:
                    mask_pixels[x, y] = 255
        
        if verbose:
            print(f"  Generated mask (black on white)")
    
    return mask

def should_skip_file(filename, skip_words):
    """Check if filename contains skip words"""
    if not skip_words:
        return False
    
    filename_lower = filename.lower()
    for word in skip_words:
        if word.lower() in filename_lower:
            return True
    return False

def process_single_image(input_path, output_path, damage_percent, scatter_mode, scatter_amount, min_piece_size, seed_value, generate_mask, invert_mask, verbose):
    """Process one image"""
    if verbose:
        print(f"\nProcessing: {input_path}")
    
    try:
        image = Image.open(input_path)
    except Exception as e:
        print(f"Error: {e}")
        return False
    
    width, height = image.size
    original_mode = image.mode
    
    if verbose:
        print(f"  Size: {width}x{height}, Mode: {original_mode}")
        print(f"  Damage: {damage_percent}%, Scatter: {scatter_mode}")
        if generate_mask:
            print(f"  Mask: {'inverted (white on transparent)' if invert_mask else 'normal (black on white)'}")
    
    # Apply damage
    damaged = apply_aggressive_damage(image, damage_percent, seed_value, verbose)
    
    # Scatter pieces if enabled
    if scatter_mode:
        pieces = find_connected_pieces(damaged, min_piece_size, verbose)
        if pieces:
            damaged = scatter_pieces(damaged, pieces, scatter_amount, seed_value, verbose)
        else:
            if verbose:
                print("  No pieces to scatter")
    
    # Convert for output
    if original_mode == 'RGB':
        damaged_output = damaged.convert('RGB')
    else:
        damaged_output = damaged
    
    # Save texture
    try:
        if not output_path.lower().endswith('.png'):
            output_path = os.path.splitext(output_path)[0] + '.png'
        
        damaged_output.save(output_path, 'PNG')
        
        if verbose:
            print(f"  ✓ Saved texture: {output_path}")
    except Exception as e:
        print(f"  ✗ Error saving: {e}")
        return False
    
    # Generate mask
    if generate_mask:
        mask = generate_mask_from_damaged(damaged, invert_mask, verbose)
        
        base_path = os.path.splitext(output_path)[0]
        mask_path = base_path + "_mask.png"  # Always use PNG for masks
        
        try:
            mask.save(mask_path, 'PNG')
            if verbose:
                print(f"  ✓ Saved mask: {mask_path}")
        except Exception as e:
            print(f"  ✗ Error saving mask: {e}")
            return False
    
    return True

def process_batch(input_folder, output_folder, damage_percent, scatter_mode, scatter_amount, min_piece_size, seed_value, generate_mask, invert_mask, verbose, recursive, suffix, skip_existing, skip_words):
    """Batch process"""
    extensions = ['*.png', '*.jpg', '*.jpeg', '*.bmp']
    
    image_files = []
    for ext in extensions:
        if recursive:
            pattern = os.path.join(input_folder, "**", ext)
            image_files.extend(glob.glob(pattern, recursive=True))
        else:
            pattern = os.path.join(input_folder, ext)
            image_files.extend(glob.glob(pattern))
    
    image_files = list(set(image_files))
    
    if not image_files:
        print("No images found")
        return
    
    print(f"Found {len(image_files)} images")
    if skip_words:
        print(f"Skip words: {', '.join(skip_words)}")
    if generate_mask:
        print(f"Mask type: {'inverted (white on transparent)' if invert_mask else 'normal (black on white)'}")
    print()
    
    processed = 0
    skipped = 0
    filtered = 0
    errors = 0
    
    for img_file in image_files:
        rel_path = os.path.relpath(img_file, input_folder)
        base_name = os.path.splitext(rel_path)[0]
        
        if should_skip_file(os.path.basename(img_file), skip_words):
            print(f"[{processed + skipped + filtered + errors + 1}/{len(image_files)}] Filtered: {rel_path}")
            filtered += 1
            continue
        
        output_rel_path = f"{base_name}{suffix}.png"
        output_path = os.path.join(output_folder, output_rel_path)
        
        os.makedirs(os.path.dirname(output_path), exist_ok=True)
        
        if skip_existing and os.path.exists(output_path):
            print(f"[{processed + skipped + filtered + errors + 1}/{len(image_files)}] Exists: {rel_path}")
            skipped += 1
            continue
        
        print(f"[{processed + skipped + filtered + errors + 1}/{len(image_files)}] {rel_path}")
        
        if process_single_image(img_file, output_path, damage_percent, scatter_mode, scatter_amount, min_piece_size, seed_value, generate_mask, invert_mask, verbose):
            processed += 1
        else:
            errors += 1
    
    print(f"\n{'='*60}")
    print(f"Complete!")
    print(f"Processed:  {processed}")
    if filtered > 0:
        print(f"Filtered:   {filtered}")
    if skipped > 0:
        print(f"Existed:    {skipped}")
    if errors > 0:
        print(f"Errors:     {errors}")
    print(f"{'='*60}")

def get_project_root():
    current_dir = os.getcwd()
    if os.path.exists(os.path.join(current_dir, "Assets")):
        return current_dir
    check_dir = current_dir
    for _ in range(5):
        parent = os.path.dirname(check_dir)
        if parent == check_dir:
            break
        if os.path.exists(os.path.join(parent, "Assets")):
            return parent
        check_dir = parent
    return current_dir

def get_folder_input(prompt, default_dir):
    print(f"\n{prompt}")
    print(f"Default: {default_dir}")
    user_input = input("Path (Enter for default): ").strip()
    return default_dir if not user_input else user_input.strip('"').strip("'")

if __name__ == "__main__":
    if '-h' in sys.argv or '--help' in sys.argv:
        show_help()
    
    damage_percent = 50
    scatter_mode = False
    scatter_amount = 3
    min_piece_size = 100
    seed_value = None
    verbose = False
    batch_mode = False
    batch_gui = False
    recursive = False
    suffix = "_debris"
    skip_existing = False
    generate_mask = True
    invert_mask = False
    skip_words = []
    positional_args = []
    
    i = 1
    while i < len(sys.argv):
        arg = sys.argv[i]
        
        if arg in ['-d', '--damage']:
            damage_percent = int(sys.argv[i + 1])
            i += 2
        elif arg in ['-s', '--seed']:
            seed_value = int(sys.argv[i + 1])
            i += 2
        elif arg == '--suffix':
            suffix = sys.argv[i + 1]
            i += 2
        elif arg == '--scatter':
            scatter_mode = True
            i += 1
        elif arg == '--scatter-amount':
            scatter_amount = int(sys.argv[i + 1])
            i += 2
        elif arg == '--min-piece-size':
            min_piece_size = int(sys.argv[i + 1])
            i += 2
        elif arg == '--skip-words':
            skip_words = [w.strip() for w in sys.argv[i + 1].split(',')]
            i += 2
        elif arg == '--skip-masks':
            skip_words = ['mask', 'Mask', 'MASK']
            i += 1
        elif arg in ['-v', '--verbose']:
            verbose = True
            i += 1
        elif arg == '--no-mask':
            generate_mask = False
            i += 1
        elif arg == '--auto-mask':
            generate_mask = True
            i += 1
        elif arg == '--invert-mask':
            invert_mask = True
            i += 1
        elif arg in ['-b', '--batch']:
            batch_mode = True
            i += 1
        elif arg == '--batch-gui':
            batch_mode = True
            batch_gui = True
            i += 1
        elif arg in ['-r', '--recursive']:
            recursive = True
            i += 1
        elif arg == '--skip-existing':
            skip_existing = True
            i += 1
        else:
            positional_args.append(arg)
            i += 1
    
    if batch_mode:
        if batch_gui or len(positional_args) < 2:
            print("\n" + "="*60)
            print("BATCH MODE")
            print("="*60)
            
            project_root = get_project_root()
            
            default_input = os.path.join(project_root, "Assets", "Game Objects", "UI", "Sensors")
            if not os.path.exists(default_input):
                default_input = os.path.join(project_root, "Assets", "Textures")
            if not os.path.exists(default_input):
                default_input = project_root
            
            input_folder = get_folder_input("[INPUT] Textures:", default_input)
            if not input_folder or not os.path.isdir(input_folder):
                print("Error: Invalid input")
                sys.exit(1)
            
            print(f"✓ Input: {input_folder}")
            
            default_output = os.path.join(project_root, "Assets", "Game Objects", "UI", "Sensors Debris")
            output_folder = get_folder_input("[OUTPUT] Debris:", default_output)
            if not output_folder:
                print("Error: No output")
                sys.exit(1)
            
            print(f"✓ Output: {output_folder}")
        else:
            input_folder = positional_args[0]
            output_folder = positional_args[1]
        
        if not os.path.isdir(input_folder):
            print("Error: Input not found")
            sys.exit(1)
        
        print(f"\n{'='*60}")
        print(f"Damage:       {damage_percent}%")
        print(f"Scatter:      {scatter_mode}")
        if scatter_mode:
            print(f"  Amount:     {scatter_amount}/10")
            print(f"  Min size:   {min_piece_size}px")
        if seed_value:
            print(f"Seed:         {seed_value}")
        print(f"Generate mask: {generate_mask}")
        if generate_mask:
            print(f"Invert mask:  {invert_mask}")
        if skip_words:
            print(f"Skip words:   {', '.join(skip_words)}")
        print(f"{'='*60}")
        
        process_batch(input_folder, output_folder, damage_percent, scatter_mode, scatter_amount, min_piece_size, seed_value, generate_mask, invert_mask, verbose, recursive, suffix, skip_existing, skip_words)
    else:
        if len(positional_args) < 2:
            print("Error: Need input and output")
            sys.exit(1)
        
        input_path = positional_args[0]
        output_path = positional_args[1]
        
        if not os.path.isfile(input_path):
            print("Error: File not found")
            sys.exit(1)
        
        if process_single_image(input_path, output_path, damage_percent, scatter_mode, scatter_amount, min_piece_size, seed_value, generate_mask, invert_mask, verbose):
            print("\n✓ Done!")
        else:
            print("\n✗ Failed")
            sys.exit(1)
