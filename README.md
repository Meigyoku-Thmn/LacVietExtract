# LacVietExtract

You have to install some more dependencies outside of this repo:
## NodeJS
Install this to run frida
```bash
npm i -g ts-node
```

## C++
Install vcpkg somewhere:
```bash
git clone https://github.com/microsoft/vcpkg
.\vcpkg\bootstrap-vcpkg.bat
.\vcpkg\vcpkg integrate install
```
Install some packages on it:
```bash
cd vcpkg
./vcpkg install capstone[x86]
./vcpkg install p-ranav-csv2
```
