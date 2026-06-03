import sys
import os
from PIL import Image

def convert_png_to_ico(png_path, ico_path):
    if not os.path.exists(png_path):
        print(f"Error: PNG file not found at {png_path}")
        return False
        
    try:
        img = Image.open(png_path)
        # Ensure icon sizes: 16x16, 32x32, 48x48, 64x64, 128x128, 256x256
        sizes = [(16, 16), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)]
        
        # Save as ICO
        img.save(ico_path, format='ICO', sizes=sizes)
        print(f"Successfully converted {png_path} to multi-resolution ICO at {ico_path}")
        return True
    except Exception as e:
        print(f"Failed to convert: {e}")
        return False

if __name__ == "__main__":
    if len(sys.argv) < 3:
        print("Usage: python convert_icon.py <input_png_path> <output_ico_path>")
        sys.exit(1)
        
    input_png = sys.argv[1]
    output_ico = sys.argv[2]
    
    success = convert_png_to_ico(input_png, output_ico)
    if not success:
        sys.exit(1)
