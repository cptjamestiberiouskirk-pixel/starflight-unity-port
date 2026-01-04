import bpy
import sys
import random
import os
import glob

def show_help():
    help_text = """
========================================
Blender Ship Debris Generator
========================================

DESCRIPTION:
  Automatically converts spaceship FBX models into damaged debris variants by
  randomly removing faces and recalculating normals. Designed for Unity game
  development workflows. Supports single file or batch folder processing.

USAGE:
  Single File:
    blender --background --python process_ship_debris.py -- [options] <input_fbx> <output_fbx>
    blender --python process_ship_debris.py -- [options] [input_fbx] [output_fbx]
  
  Batch Folder:
    blender --background --python process_ship_debris.py -- [options] --batch <input_folder> <output_folder>
    blender --python process_ship_debris.py -- [options] --batch-gui

  Note: Omit --background flag to use GUI file dialogs if paths not provided

OPTIONS:
  -h, --help              Show this help message and exit
  -d, --damage PERCENT    Percentage of faces to delete (0-100, default: 30)
                          Higher values = more destruction
  -s, --seed NUMBER       Random seed for reproducible results (default: random)
                          Use same seed to generate identical debris each time
  -v, --verbose           Print detailed processing information for each mesh
  -g, --gui               Force GUI file dialogs even if running in background
  -b, --batch             Enable batch processing mode for entire folders
  --batch-gui             Enable batch mode with interactive folder prompts
  -r, --recursive         Search for FBX files recursively in subfolders
  --suffix SUFFIX         Suffix to append to output files (default: _debris)
  --skip-existing         Skip processing if output file already exists

ARGUMENTS:
  input_fbx               Path to input FBX file (optional - dialog opens if not provided)
  output_fbx              Path to output debris FBX file (optional - dialog opens if not provided)
  input_folder            Path to folder containing FBX files (batch mode)
  output_folder           Path to output folder for debris files (batch mode)

========================================
SINGLE FILE EXAMPLES:
========================================

1. Basic usage with default 30% damage:
   blender --background --python process_ship_debris.py -- ship.fbx debris.fbx

2. Light damage (15% destruction):
   blender --background --python process_ship_debris.py -- -d 15 intact_ship.fbx light_damage.fbx

3. Heavy damage (60% destruction):
   blender --background --python process_ship_debris.py -- -d 60 ship.fbx heavy_debris.fbx

4. Interactive mode with file dialogs:
   blender --python process_ship_debris.py -- --damage 50 --verbose

========================================
BATCH PROCESSING EXAMPLES:
========================================

5. Batch mode with interactive folder prompts:
   blender --python process_ship_debris.py -- --batch-gui

6. Batch mode with prompts and custom damage:
   blender --python process_ship_debris.py -- -d 40 -r --batch-gui

7. Process all FBX files in a folder (non-recursive):
   blender --background --python process_ship_debris.py -- --batch "C:\\Ships" "C:\\Debris"

8. Process all FBX files recursively with custom damage:
   blender --background --python process_ship_debris.py -- -d 40 --batch --recursive "E:\\Ships" "E:\\Debris"

9. Process with custom suffix:
   blender --background --python process_ship_debris.py -- --batch --suffix _damaged "C:\\Ships" "C:\\Debris"

10. Process folder with verbose output:
    blender --background --python process_ship_debris.py -- -v --batch --recursive "C:\\Ships" "C:\\Debris"

11. Skip already processed files:
    blender --background --python process_ship_debris.py -- --batch --skip-existing --recursive "C:\\Ships" "C:\\Debris"

12. Batch process with reproducible seed (all files get same damage pattern):
    blender --background --python process_ship_debris.py -- -s 12345 --batch -r "C:\\Ships" "C:\\Debris"

========================================
ADVANCED EXAMPLES:
========================================

13. Interactive batch with all options:
    blender --python process_ship_debris.py -- -d 50 -r -v --suffix _wreckage --batch-gui

14. Batch process Unity project structure:
    blender --background --python process_ship_debris.py -- --batch --recursive ^
      "E:\\Unity Projects\\MyGame\\Assets\\Ships" ^
      "E:\\Unity Projects\\MyGame\\Assets\\Debris"

15. Heavy destruction for all ships with custom suffix:
    blender --background --python process_ship_debris.py -- -d 70 --suffix _wreckage --batch -r "C:\\Ships" "C:\\Wreckage"

16. Reproducible debris with seed (same output every time):
    blender --background --python process_ship_debris.py -- --seed 42 ship.fbx debris.fbx

17. Create multiple damage variants using different seeds:
    blender --background --python process_ship_debris.py -- -s 100 --suffix _v1 --batch "C:\\Ships" "C:\\Debris"
    blender --background --python process_ship_debris.py -- -s 200 --suffix _v2 --batch "C:\\Ships" "C:\\Debris"
    blender --background --python process_ship_debris.py -- -s 300 --suffix _v3 --batch "C:\\Ships" "C:\\Debris"

========================================
TIPS:
========================================

  - Use damage values between 20-40% for realistic battle damage
  - Use damage values 50%+ for destroyed/exploded ship pieces
  - Use damage values under 15% for minor cosmetic damage
  - Save seeds if you need to regenerate the exact same debris later
  - Use --skip-existing for incremental processing of large folders
  - Use --batch-gui for easy folder selection with console prompts
  - Batch mode preserves folder structure when using --recursive
  - Output directly to Unity Assets folder for automatic import
  - Press Enter at prompts to use suggested default paths

========================================
"""
    print(help_text)
    sys.exit(0)

def process_mesh(obj, damage_percent, verbose):
    """Process a single mesh object"""
    if verbose:
        print(f"  Processing mesh: {obj.name}")
    
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.select_all(action='DESELECT')
    obj.select_set(True)
    
    mesh = obj.data
    original_faces = len(mesh.polygons)
    
    if verbose:
        print(f"    Original faces: {original_faces}")
    
    # Switch to edit mode
    bpy.ops.object.mode_set(mode='EDIT')
    bpy.ops.mesh.select_all(action='DESELECT')
    
    # Switch to object mode to manipulate selection directly
    bpy.ops.object.mode_set(mode='OBJECT')
    
    # Manually select faces to delete based on damage percentage
    num_to_delete = int(len(mesh.polygons) * (damage_percent / 100.0))
    
    if num_to_delete > 0:
        faces_to_delete = random.sample(range(len(mesh.polygons)), num_to_delete)
        
        for face_idx in faces_to_delete:
            mesh.polygons[face_idx].select = True
        
        # Back to edit mode to delete selected faces
        bpy.ops.object.mode_set(mode='EDIT')
        bpy.ops.mesh.delete(type='FACE')
    else:
        bpy.ops.object.mode_set(mode='EDIT')
    
    # Check remaining faces
    bpy.ops.object.mode_set(mode='OBJECT')
    remaining_faces = len(mesh.polygons)
    
    if verbose:
        print(f"    Remaining faces: {remaining_faces} ({100*(remaining_faces/original_faces):.1f}%)")
    
    # Add some vertex displacement for damage effect
    bpy.ops.object.mode_set(mode='EDIT')
    bpy.ops.mesh.select_all(action='SELECT')
    
    # Recalculate normals
    bpy.ops.mesh.normals_make_consistent(inside=False)
    
    bpy.ops.object.mode_set(mode='OBJECT')
    mesh.update()

def process_single_file(input_path, output_path, damage_percent, verbose):
    """Process a single FBX file"""
    # Clear default scene
    bpy.ops.object.select_all(action='SELECT')
    bpy.ops.object.delete()
    
    # Import FBX
    print(f"Importing: {input_path}")
    bpy.ops.import_scene.fbx(filepath=input_path)
    
    # Process each mesh object
    for obj in bpy.context.scene.objects:
        if obj.type == 'MESH':
            process_mesh(obj, damage_percent, verbose)
    
    # Export as FBX
    bpy.ops.object.select_all(action='SELECT')
    bpy.ops.export_scene.fbx(
        filepath=output_path,
        axis_forward='-Z',
        axis_up='Y',
        use_selection=True,
        apply_scale_options='FBX_SCALE_ALL'
    )
    
    print(f"Export complete: {output_path}")

def process_batch(input_folder, output_folder, damage_percent, verbose, recursive, suffix, skip_existing, seed_value):
    """Process all FBX files in a folder"""
    # Find all FBX files
    if recursive:
        pattern = os.path.join(input_folder, "**", "*.fbx")
        fbx_files = glob.glob(pattern, recursive=True)
        # Also get uppercase FBX
        pattern_upper = os.path.join(input_folder, "**", "*.FBX")
        fbx_files.extend(glob.glob(pattern_upper, recursive=True))
    else:
        pattern = os.path.join(input_folder, "*.fbx")
        fbx_files = glob.glob(pattern)
        pattern_upper = os.path.join(input_folder, "*.FBX")
        fbx_files.extend(glob.glob(pattern_upper))
    
    # Remove duplicates
    fbx_files = list(set(fbx_files))
    
    if not fbx_files:
        print(f"No FBX files found in {input_folder}")
        return
    
    print(f"Found {len(fbx_files)} FBX file(s) to process")
    
    processed = 0
    skipped = 0
    
    for fbx_file in fbx_files:
        # Calculate relative path and output path
        rel_path = os.path.relpath(fbx_file, input_folder)
        base_name = os.path.splitext(rel_path)[0]
        output_rel_path = f"{base_name}{suffix}.fbx"
        output_path = os.path.join(output_folder, output_rel_path)
        
        # Create output directory if needed
        output_dir = os.path.dirname(output_path)
        os.makedirs(output_dir, exist_ok=True)
        
        # Check if should skip
        if skip_existing and os.path.exists(output_path):
            print(f"Skipping (already exists): {rel_path}")
            skipped += 1
            continue
        
        print(f"\n[{processed + skipped + 1}/{len(fbx_files)}] Processing: {rel_path}")
        
        # Set seed for this file if provided
        if seed_value is not None:
            random.seed(seed_value)
        
        try:
            process_single_file(fbx_file, output_path, damage_percent, verbose)
            processed += 1
        except Exception as e:
            print(f"ERROR processing {rel_path}: {e}")
    
    print(f"\n{'='*50}")
    print(f"Batch processing complete!")
    print(f"Processed: {processed} file(s)")
    if skipped > 0:
        print(f"Skipped: {skipped} file(s)")
    print(f"{'='*50}")

def get_project_root():
    """Try to find the project root directory"""
    # Start with current working directory
    current_dir = os.getcwd()
    
    # Check if we're in a Unity project
    if os.path.exists(os.path.join(current_dir, "Assets")):
        return current_dir
    
    # Check parent directories for Unity project
    check_dir = current_dir
    for _ in range(5):  # Check up to 5 levels up
        parent = os.path.dirname(check_dir)
        if parent == check_dir:  # Reached root
            break
        if os.path.exists(os.path.join(parent, "Assets")):
            return parent
        check_dir = parent
    
    # Default to current directory
    return current_dir

def get_folder_input(prompt, default_dir):
    """Get folder path via console input"""
    print(f"\n{prompt}")
    print(f"Default: {default_dir}")
    user_input = input("Enter folder path (or press Enter for default): ").strip()
    
    if not user_input:
        return default_dir
    
    # Remove quotes if user copied path with quotes
    user_input = user_input.strip('"').strip("'")
    
    return user_input

# Parse arguments after the '--' separator
try:
    separator_index = sys.argv.index('--')
    script_args = sys.argv[separator_index + 1:]
except ValueError:
    script_args = []

# Check for help flag
if '-h' in script_args or '--help' in script_args:
    show_help()

# Parse optional arguments
damage_percent = 30
seed_value = None
verbose = False
use_gui = False
batch_mode = False
batch_gui = False
recursive = False
suffix = "_debris"
skip_existing = False
positional_args = []

i = 0
while i < len(script_args):
    arg = script_args[i]
    
    if arg in ['-d', '--damage']:
        if i + 1 < len(script_args):
            damage_percent = int(script_args[i + 1])
            i += 2
        else:
            print("Error: --damage requires a value")
            sys.exit(1)
    elif arg in ['-s', '--seed']:
        if i + 1 < len(script_args):
            seed_value = int(script_args[i + 1])
            i += 2
        else:
            print("Error: --seed requires a value")
            sys.exit(1)
    elif arg == '--suffix':
        if i + 1 < len(script_args):
            suffix = script_args[i + 1]
            i += 2
        else:
            print("Error: --suffix requires a value")
            sys.exit(1)
    elif arg in ['-v', '--verbose']:
        verbose = True
        i += 1
    elif arg in ['-g', '--gui']:
        use_gui = True
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

# Validate damage percentage
if damage_percent < 0 or damage_percent > 100:
    print(f"Error: Damage percentage must be between 0 and 100 (got {damage_percent})")
    sys.exit(1)

# Set random seed if provided (for single file mode)
if seed_value is not None and not batch_mode:
    random.seed(seed_value)
    if verbose:
        print(f"Using random seed: {seed_value}")

# BATCH MODE
if batch_mode:
    input_folder = None
    output_folder = None
    
    # Get folders from command line or prompts
    if batch_gui or len(positional_args) < 2:
        print("\n" + "="*60)
        print("BATCH MODE - FOLDER SELECTION")
        print("="*60)
        
        # Get project root as initial directory
        project_root = get_project_root()
        print(f"\nProject root detected: {project_root}")
        
        # Get input folder
        default_input = os.path.join(project_root, "Assets", "Game Objects", "Ships")
        if not os.path.exists(default_input):
            default_input = project_root
        
        input_folder = get_folder_input(
            "\n[INPUT FOLDER] Select folder containing ship FBX files:",
            default_input
        )
        
        if not input_folder or not os.path.isdir(input_folder):
            print(f"Error: Invalid input folder: {input_folder}")
            sys.exit(1)
        
        print(f"✓ Input folder: {input_folder}")
        
        # Get output folder
        default_output = os.path.join(project_root, "Assets", "Game Objects", "Ships Debris")
        output_folder = get_folder_input(
            "\n[OUTPUT FOLDER] Select folder where debris files will be saved:",
            default_output
        )
        
        if not output_folder:
            print("Error: No output folder specified.")
            sys.exit(1)
        
        print(f"✓ Output folder: {output_folder}")
    else:
        input_folder = positional_args[0]
        output_folder = positional_args[1]
    
    if not os.path.isdir(input_folder):
        print(f"Error: Input folder does not exist: {input_folder}")
        sys.exit(1)
    
    print(f"\n{'='*60}")
    print(f"BATCH PROCESSING CONFIGURATION")
    print(f"{'='*60}")
    print(f"Input folder:  {input_folder}")
    print(f"Output folder: {output_folder}")
    print(f"Recursive:     {recursive}")
    print(f"Damage:        {damage_percent}%")
    print(f"Suffix:        {suffix}")
    if seed_value:
        print(f"Seed:          {seed_value}")
    if skip_existing:
        print(f"Skip existing: Yes")
    print(f"{'='*60}\n")
    
    process_batch(input_folder, output_folder, damage_percent, verbose, recursive, suffix, skip_existing, seed_value)
    sys.exit(0)

# SINGLE FILE MODE
input_path = None
output_path = None

if len(positional_args) >= 2:
    input_path = positional_args[0]
    output_path = positional_args[1]
elif len(positional_args) == 1:
    input_path = positional_args[0]

# Use file dialogs if paths not provided
if input_path is None or use_gui:
    print("Opening file dialog for input FBX...")
    try:
        import tkinter as tk
        from tkinter import filedialog
        
        root = tk.Tk()
        root.withdraw()
        root.attributes('-topmost', True)
        
        project_root = get_project_root()
        
        input_path = filedialog.askopenfilename(
            title="Select Input Ship FBX File",
            filetypes=[("FBX files", "*.fbx *.FBX"), ("All files", "*.*")],
            initialdir=project_root
        )
        
        if not input_path:
            print("No input file selected. Exiting.")
            sys.exit(1)
            
        print(f"Selected input: {input_path}")
        
    except ImportError:
        print("Error: tkinter not available. Using console input.")
        project_root = get_project_root()
        input_path = get_folder_input("Enter input FBX file path:", project_root)
        
        if not input_path or not os.path.isfile(input_path):
            print(f"Error: Invalid input file: {input_path}")
            sys.exit(1)

if output_path is None or use_gui:
    print("Opening file dialog for output FBX...")
    try:
        import tkinter as tk
        from tkinter import filedialog
        
        root = tk.Tk()
        root.withdraw()
        root.attributes('-topmost', True)
        
        # Suggest default output name based on input
        input_dir = os.path.dirname(input_path)
        input_name = os.path.splitext(os.path.basename(input_path))[0]
        suggested_name = f"{input_name}{suffix}.fbx"
        
        output_path = filedialog.asksaveasfilename(
            title="Save Output Debris FBX File",
            filetypes=[("FBX files", "*.fbx"), ("All files", "*.*")],
            initialdir=input_dir,
            initialfile=suggested_name,
            defaultextension=".fbx"
        )
        
        if not output_path:
            print("No output file specified. Exiting.")
            sys.exit(1)
            
        print(f"Selected output: {output_path}")
        
    except ImportError:
        print("Error: tkinter not available. Using console input.")
        # Suggest default output name based on input
        input_dir = os.path.dirname(input_path)
        input_name = os.path.splitext(os.path.basename(input_path))[0]
        suggested_path = os.path.join(input_dir, f"{input_name}{suffix}.fbx")
        
        output_path = get_folder_input("Enter output FBX file path:", suggested_path)
        
        if not output_path:
            print("Error: No output file specified.")
            sys.exit(1)

# Process single file
process_single_file(input_path, output_path, damage_percent, verbose)
print(f"Damage applied: {damage_percent}% of faces removed")
