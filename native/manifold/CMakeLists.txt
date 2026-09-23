# Builds Manifold (Apache-2.0, fetched into src/ by build.ps1) as ONE self-contained DLL for the
# Unity app. The Manifold core is a static library, and the upstream C bindings (manifoldc) are
# compiled into manifoldc.dll with the static MSVC runtime. The Unity player itself has no
# Visual C++ runtime dependency, so neither may this plugin: players must not need the
# Visual C++ Redistributable, and one DLL means one C runtime heap for every allocation.
cmake_minimum_required(VERSION 3.21)
project(coreengine_manifold LANGUAGES CXX)

set(CMAKE_MSVC_RUNTIME_LIBRARY "MultiThreaded$<$<CONFIG:Debug>:Debug>")

set(BUILD_SHARED_LIBS OFF CACHE BOOL "Manifold core as a static library" FORCE)
set(MANIFOLD_CROSS_SECTION ON CACHE BOOL "" FORCE)
# boolean2 is Manifold's built-in 2D backend; clipper2 would be downloaded at configure time.
set(MANIFOLD_CROSS_SECTION_BACKEND boolean2 CACHE STRING "" FORCE)
set(MANIFOLD_CBIND OFF CACHE BOOL "The C bindings are built below as our own shared target" FORCE)
set(MANIFOLD_DOWNLOADS OFF CACHE BOOL "" FORCE)
set(MANIFOLD_TEST OFF CACHE BOOL "" FORCE)
set(MANIFOLD_PAR OFF CACHE BOOL "" FORCE)
set(MANIFOLD_PYBIND OFF CACHE BOOL "" FORCE)

add_subdirectory(src manifold EXCLUDE_FROM_ALL)

set(CBIND ${CMAKE_CURRENT_SOURCE_DIR}/src/bindings/c)
add_library(
  manifoldc
  SHARED
  ${CBIND}/manifoldc.cpp
  ${CBIND}/conv.cpp
  ${CBIND}/box.cpp
  ${CBIND}/cross.cpp
  ${CBIND}/rect.cpp
)
target_include_directories(manifoldc PRIVATE ${CBIND} ${CBIND}/include)
target_link_libraries(manifoldc PRIVATE manifold)
# The C header has no export macros; export every function defined in the binding sources.
set_target_properties(manifoldc PROPERTIES WINDOWS_EXPORT_ALL_SYMBOLS ON)
