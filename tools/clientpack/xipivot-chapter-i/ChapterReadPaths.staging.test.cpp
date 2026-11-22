#include "XIPivot.Core/src/Redirector.h"
#include <algorithm>
#include <cstdio>
#include <filesystem>
#include <fstream>
#include <stdexcept>

namespace F = std::filesystem;
using XiPivot::Core::Redirector;
using XiPivot::Core::ChapterReadPaths;
int checks = 0;
void check(bool pass, const char* name) {
    ++checks;
    if (!pass) { fprintf(stderr, "FAIL %s\n", name); std::exit(1); }
}
void put(const F::path& path, const char* text) {
    F::create_directories(path.parent_path());
    std::ofstream(path, std::ios::binary) << text;
}
std::string kernel(const F::path& path, DWORD a=GENERIC_READ, DWORD c=OPEN_EXISTING){
    char bytes[64] {};
    DWORD n = 0;
    auto h=CreateFileA(path.string().c_str(),a,FILE_SHARE_READ,nullptr,c,FILE_ATTRIBUTE_NORMAL,nullptr);
    if(h==INVALID_HANDLE_VALUE) return {};
    ReadFile(h,bytes,sizeof(bytes),&n,nullptr);
    CloseHandle(h);
    return std::string(bytes,n);
}
std::string stdio(const F::path& path, const char* mode="rb"){
    FILE* f=nullptr;
    if (fopen_s(&f,path.string().c_str(),mode)!=0 || !f) return {};
    char bytes[64];
    const auto n=fread(bytes,1,sizeof(bytes),f);
    fclose(f);
    return std::string(bytes,n);
}
int main(int argc,char** argv){
    if (argc != 2) return 2;
    const F::path root = F::absolute(argv[1]), game=root/"game", retail=root/"external", data=root/"DATs";
    const auto overlay=data/"vanadreams-chapter-i";
    for(const char* p : {"FTABLE.DAT","VTABLE.DAT","ROM/118/114.DAT","ROM2/FTABLE2.DAT","ROM/490/55.DAT"}) {
        put(game/p,"base"); put(retail/p,"retail"); put(overlay/p,"chapter-mod");
    }
    ChapterReadPaths alias;
    check(!alias.lookup((game/"FTABLE.DAT").string().c_str()), "initially disabled");
    check(!alias.set(game.string(),data.string(),"../vanadreams-chapter-i"), "traversal overlay rejected");
    check(!alias.set(game.string(),"relative","vanadreams-chapter-i"), "relative DAT root rejected");
    check(alias.set(game.string(),data.string(),"vanadreams-chapter-i"), "valid exactly paired fixture");
    check(!alias.lookup((retail/"FTABLE.DAT").string().c_str()), "other game root untouched");
    check(!alias.lookup((game/"ROM2/FTABLE2.DAT").string().c_str()), "expansion rule not special-cased");
    check(!alias.lookup(("\\\\?\\"+(game/"FTABLE.DAT").string()).c_str()), "namespace excluded");
    check(!alias.lookup((game/"FOOFTABLE.DAT").string().c_str()), "basename substring cannot match");
    check(alias.lookup((game/"ftable.dat").string().c_str()) != nullptr, "Win32 case-insensitivity");
    check(!ChapterReadPaths::readOnly(GENERIC_WRITE, OPEN_EXISTING,0), "write denied");
    check(!ChapterReadPaths::readOnly(GENERIC_READ, CREATE_ALWAYS,0), "create/truncate denied");
    check(!ChapterReadPaths::readOnly(GENERIC_READ, OPEN_EXISTING,FILE_FLAG_DELETE_ON_CLOSE), "delete-on-close denied");
    check(!ChapterReadPaths::readOnly("r+b"), "stdio update denied");
    auto& r=Redirector::instance();
    r.setRootPath(data.string());
    r.setRedirectFOpenS(false); // no ordinary pre-D3D POSIX hook, exact aliases are independent.
    check(r.setChapterReadPaths(game.string(),"vanadreams-chapter-i"),"install aliases");
    check(kernel(game/"FTABLE.DAT")=="base", "game stock baseline");
    check(r.setupHooks(), "actual existing Detours attach");
    for(const char* p : {"FTABLE.DAT","VTABLE.DAT","ROM/118/114.DAT"}) {
        check(kernel(game/p)=="chapter-mod",p);
        check(stdio(game/p)=="chapter-mod",p);
        check(kernel(retail/p)=="retail","other client kernel path");
        check(stdio(retail/p)=="retail","other client stdio path");
        check(kernel(game/p,GENERIC_READ|GENERIC_WRITE)=="base","read-write kernel remains original");
        check(stdio(game/p,"r+b")=="base","read-write stdio remains original");
    }
    check(kernel(F::path(game.string()+"//ROM/118/114.DAT"))=="chapter-mod", "engine slashes");
    WIN32_FIND_DATAA attrs {};
    const auto h=FindFirstFileA((game/"FTABLE.DAT").string().c_str(), &attrs);
    check(h!=INVALID_HANDLE_VALUE && attrs.nFileSizeLow==11,"exact boot file lookup metadata");
    FindClose(h);
    check(kernel(game/"ROM/490/55.DAT")=="base","normal path unaffected without normal overlay");
    check(!r.releaseHooks(),"official return reports no hooks remaining");
    check(kernel(game/"FTABLE.DAT")=="base" && stdio(game/"VTABLE.DAT")=="base","unchanged originals");
    printf("%d native exact-path and actual pre-D3D hook assertions passed.\n",checks);
}