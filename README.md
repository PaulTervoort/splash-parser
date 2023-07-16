# splash-parser
Command line program to parse and modify Android splash.img files.

# Build
This project should be built using Visual Studio by opening the project and press Ctrl+B to build the executables. The result will be in a subfolder of './bin'.

# Disclaimer
The program is verified to work for all devices it is tested on, but this does not guarantee the correctness of the program. Use at your own risk, and always make sure to keep a backup of the original splash.img file.

# Usage
**IMPORTANT** The input files will never be modified, but the output files overwrite any existing file with the same name, so rename outputs you want to keep. It is recommended to place files for use with this program in a separate folder to avoid modifying unrelated files.

## Parsing
To parse a splash.img file and extract the images it contains, execute SplashParser.exe with as argument the file to parse. The parsed images will have the same name as the input file, but postfixed with '_' + \<img_index\>.

## Modifying
The images in a splash.img file can be replaced one at a time with another image with the same dimensions. To do this, execute SplashParser.exe with as first argument the splash.img to modify and as second argument the new image. The third argument is the image index of the image to replace, which corresponds to the image index of one of the images parsed from the same splash.img file. The result is a new splash.img file postfixed with '_MOD' + \<img_index\>.

Because splash.img uses compression, the substituted image can be larger or smaller in size. If the new image is smaller, any remaining bytes of the substituted image are set to 0. If the new image is larger, only pages of all 0 are overwritten and otherwise an error is thrown. **ONLY** data of the substituted image or all 0 pages is modified, other data is left untouched for compatibility.
