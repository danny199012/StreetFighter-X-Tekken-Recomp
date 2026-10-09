# SFxT - ReXGlue Recompiled Project
#
# SDK boilerplate lives in generated/rexglue.cmake.

cmake_minimum_required(VERSION 3.25)
project(SFxT LANGUAGES CXX)

set(CMAKE_CXX_STANDARD 23)
set(CMAKE_CXX_STANDARD_REQUIRED ON)

include(generated/rexglue.cmake)

# Sources
set(SFXT_SOURCES
    src/main.cpp
    src/sfxt_profile.cpp
)

if(WIN32)
    add_executable(SFxT WIN32 ${SFXT_SOURCES})
else()
    add_executable(SFxT ${SFXT_SOURCES})
endif()

rexglue_setup_target(SFxT)
