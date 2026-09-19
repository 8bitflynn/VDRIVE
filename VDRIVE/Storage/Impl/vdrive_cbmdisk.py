#!/usr/bin/env python
import sys
print("ARGV:", sys.argv)
import os
import traceback

# Detect optional cbmdisk path as the last argument
lib_path = None
if len(sys.argv) > 1 and os.path.isdir(sys.argv[-1]):
    lib_path = sys.argv[-1]
    sys.path.append(lib_path)

# Import cbmdisk after sys.path adjustment
try:
    import cbmdisk
except ImportError:
    print("Error: No module named 'cbmdisk'. "
          "If you passed a cbmdisk_path, it must be a directory containing cbmdisk.pyd.")
    sys.exit(1)

def list_dir(disk_path):
    try:
        disk = cbmdisk.Disk(disk_path)
        print('0 "{}" 2a'.format(cbmdisk.to_ascii(disk.name)))
        for file in disk.files:
            name = cbmdisk.to_ascii(file.name).strip()
            print('{}  "{}"  {}'.format(file.size, name, file.type))
        print('{} BLOCKS FREE.'.format(disk.free_blocks))
    except Exception as e:
        print("Error listing directory:", e)
        traceback.print_exc()

def save_file(disk_path, filepath):
    try:
        disk = cbmdisk.Disk(disk_path)
        index = len(disk.files)
        new_file = disk.files.create(index)
        new_file.name = os.path.basename(filepath).upper()[:16]
        new_file.type = "PRG"
        with open(filepath, "rb") as f:
            new_file.bytes = f.read()
        disk.save(disk_path)
        print('Saved: {} into {}'.format(filepath, disk_path))
    except Exception as e:
        print("Error saving file:", e)
        traceback.print_exc()

def load_file(disk_path, filename, outdir=None):
    try:
        disk = cbmdisk.Disk(disk_path)
        target = disk.files.find(filename)
        if target:
            # Default output directory = same folder as the D64
            if outdir is None:
                outdir = os.path.dirname(os.path.abspath(disk_path))

            # Use the filename EXACTLY as passed in
            output_path = os.path.join(outdir, f"{filename}.prg")

            target.save(output_path)
            print('Loaded:', os.path.abspath(output_path))
        else:
            print(f'File \"{filename}\" not found.')
    except Exception as e:
        print("Error loading file:", e)
        traceback.print_exc()


def main():
    try:
        if len(sys.argv) < 3:
            print('Usage: vdrive_cbmdisk.py [dir|load|save] disk.d64 [filename] [outdir] [cbmdisk_path]')
            return

        # Build effective args (exclude lib_path if present)
        args = sys.argv[1:]
        if lib_path is not None and os.path.isdir(sys.argv[-1]):
            args = args[:-1]

        command = args[0].lower()
        disk_path = args[1]
        filename = args[2] if len(args) >= 3 else None
        outdir = args[3] if len(args) >= 4 else None

        if command == "dir":
            list_dir(disk_path)
        elif command == "load" and filename:
            load_file(disk_path, filename, outdir)
        elif command == "save" and filename:
            save_file(disk_path, filename)
        else:
            print('Invalid command or missing arguments.')
    except Exception as e:
        print("Unexpected error:", e)
        traceback.print_exc()

if __name__ == "__main__":
    main()
