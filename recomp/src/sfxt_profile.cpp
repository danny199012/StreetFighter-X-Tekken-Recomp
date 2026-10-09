// sfxt - Synthetic dashboard profile.
//
// How the override works: the static recomp emits direct host calls to the
// runtime's import implementations (`__imp__XamUserGetSigninInfo(ctx, base)`
// and friends, extern "C", resolved at link time from rexruntime's import
// library). Those symbols are deliberately left overridable - the generated
// pch notes they "stay weak so hooks can replace it" - so defining our own
// extern "C" functions with the same names in this object file makes the
// linker bind every generated call site to us instead of the prebuilt
// runtime. No dispatcher/table patching is involved.
//
// The shim bodies mirror the runtime's own XamUser* entries
// (rexglue-sdk/src/kernel/xam/xam_user.cpp) argument-for-argument and
// byte-for-byte, except the identity values come from the profile_* cvars
// (which the launcher writes into SFxT.toml, and which also show up in the
// in-game F4 editor). Getting any of this wrong corrupts guest memory - the
// struct layouts and error codes below are load-bearing.
//
// Scope, stated plainly: this gives the game a stable local identity that
// survives restarts. It is NOT an Xbox Live account and authenticates to
// nothing.
//
// Note: this game also imports XamUserReadProfileSettings(Ex); the runtime's
// implementation answers those (with the built-in identity) and is left in
// place. If the game surfaces a name sourced from profile settings rather
// than the sign-in info, a ReadProfileSettings override is the follow-up.

#include "sfxt_profile.h"

#include <cstring>
#include <string>

#include <fmt/format.h>
#include <rex/cvar.h>
#include <rex/logging.h>
#include <rex/ppc/context.h>

namespace sfxt {

// Cvars. Defined here (not in a header) so the category shows up in the SDK's
// F4 editor alongside the runtime's own settings, and so the TOML values bind
// to them during startup parsing. Defaults preserve the runtime's built-in
// identity until a value is set.
REXCVAR_DEFINE_STRING(profile_name, "User", "Street Fighter X Tekken",
                      "Gamer tag the game sees for the synthetic profile");
REXCVAR_DEFINE_UINT64(profile_xuid, 0xB13EBABEBABEBABEull, "Street Fighter X Tekken",
                      "XUID the game sees for the synthetic profile");
REXCVAR_DEFINE_BOOL(profile_signed_in, true, "Street Fighter X Tekken",
                    "Report the synthetic profile as signed in");

void RegisterProfileCVars() {
  // The REXCVAR_DEFINE_* macros register during static init; this call site
  // confirms the cvars are present and shows the active values (they are what
  // SFxT.toml binds to and what F4 lists).
  REXLOG_INFO(
      "SFxT profile cvars registered (name={}, xuid={}, signed_in={})",
      REXCVAR_GET(profile_name), REXCVAR_GET(profile_xuid),
      REXCVAR_GET(profile_signed_in));
}

Profile GetProfile() {
  Profile profile;
  profile.name = REXCVAR_GET(profile_name);
  profile.xuid = REXCVAR_GET(profile_xuid);
  profile.signed_in = REXCVAR_GET(profile_signed_in);
  return profile;
}

}  // namespace sfxt

namespace {

// X_E_* HRESULT codes (rex/system/xtypes.h): win32 error wrapped as HRESULT.
constexpr int32_t kXESuccess = 0;              // X_E_SUCCESS
constexpr int32_t kXEInvalidArg = 0x80070057;  // X_E_INVALIDARG
constexpr int32_t kXENoSuchUser = 0x80070525;  // X_E_NO_SUCH_USER

// Guest memory helper. The PowerPC guest is big-endian, hence the byte swaps.
inline uint8_t* GuestPtr(uint8_t* base, uint32_t addr) {
  const uint32_t bias = (addr >= 0xE0000000u) ? 0x1000u : 0u;
  return base + addr + bias;
}

void StoreU32(uint8_t* base, uint32_t addr, uint32_t value) {
  *reinterpret_cast<volatile uint32_t*>(GuestPtr(base, addr)) =
      __builtin_bswap32(value);
}

void StoreU64(uint8_t* base, uint32_t addr, uint64_t value) {
  *reinterpret_cast<volatile uint64_t*>(GuestPtr(base, addr)) =
      __builtin_bswap64(value);
}

// ANSI copy with truncation + NUL termination, mirroring
// rex::string::copy_truncating(dst, src, capacity).
void CopyNameTruncating(uint8_t* base, uint32_t addr, const std::string& utf8,
                        uint32_t capacity) {
  if (capacity == 0) {
    return;
  }
  const uint32_t max_chars = capacity - 1;  // room for the terminator
  const uint32_t to_copy =
      static_cast<uint32_t>(utf8.size() < max_chars ? utf8.size() : max_chars);
  uint8_t* dst = GuestPtr(base, addr);
  std::memcpy(dst, utf8.data(), to_copy);
  dst[to_copy] = 0;
}

// Logs the first few shim calls: proves which identity paths the guest
// actually uses (and what it was told) without spamming per-frame polls.
void LogShimCall(const char* fn, const std::string& detail) {
  static uint32_t total = 0;
  if (total++ < 10) {
    REXLOG_INFO("{} {}", fn, detail);
  }
}

// XamUserGetXUID(user_index, type_mask, xuid_ptr).
// r3 = user index, r4 = profile type mask, r5 = u64 xuid out-pointer.
// Mirrors XamUserGetXUID_entry: writes the xuid (or 0) through the pointer
// unconditionally once it is non-null, and returns X_E_* status.
void ShimGetXUID(PPCContext& ctx, uint8_t* base) {
  const uint32_t user_index = static_cast<uint32_t>(ctx.r3.u64);
  const uint32_t type_mask = static_cast<uint32_t>(ctx.r4.u64);
  const uint32_t xuid_ptr = static_cast<uint32_t>(ctx.r5.u64);

  const sfxt::Profile profile = sfxt::GetProfile();
  if (xuid_ptr == 0) {
    ctx.r3.s64 = kXEInvalidArg;
    return;
  }

  int32_t result = kXENoSuchUser;
  uint64_t xuid = 0;
  if (user_index < 4) {
    if (user_index == 0) {
      // UserProfile::type() reports local | online (1 | 2).
      const uint32_t type = 3u & type_mask;
      if (type & (2 | 4)) {
        xuid = profile.xuid;
        result = kXESuccess;
      } else if (type & 1) {
        xuid = profile.xuid;
        result = kXESuccess;
      }
    }
  } else {
    result = kXEInvalidArg;
  }
  StoreU64(base, xuid_ptr, xuid);
  LogShimCall("XamUserGetXUID", fmt::format("idx={} mask={:x} -> {:08X}",
                                            user_index, type_mask, result));
  ctx.r3.s64 = result;
}

// XamUserGetSigninState(user_index) -> the sign-in state directly (not a
// status code): 1 for a signed-in profile 0, 0 for everyone else.
void ShimGetSigninState(PPCContext& ctx, uint8_t* base) {
  (void)base;
  const uint32_t user_index = static_cast<uint32_t>(ctx.r3.u64);
  uint32_t state = 0;
  if (user_index < 4 && user_index == 0 && sfxt::GetProfile().signed_in) {
    state = 1;
  }
  LogShimCall("XamUserGetSigninState",
              fmt::format("idx={} -> {}", user_index, state));
  ctx.r3.s64 = state;
}

// XamUserGetName(user_index, buffer, buffer_len).
// r3 = user index, r4 = ANSI name buffer, r5 = capacity in chars.
// Mirrors XamUserGetName_entry: capacity clamped to 16.
void ShimGetName(PPCContext& ctx, uint8_t* base) {
  const uint32_t user_index = static_cast<uint32_t>(ctx.r3.u64);
  const uint32_t buffer = static_cast<uint32_t>(ctx.r4.u64);
  const uint32_t buffer_len = static_cast<uint32_t>(ctx.r5.u64);

  if (user_index >= 4) {
    ctx.r3.s64 = kXEInvalidArg;
    return;
  }
  if (user_index != 0) {
    ctx.r3.s64 = kXENoSuchUser;
    return;
  }
  const sfxt::Profile profile = sfxt::GetProfile();
  const std::string name = profile.signed_in ? profile.name : std::string();
  CopyNameTruncating(base, buffer, name, buffer_len < 16 ? buffer_len : 16);
  LogShimCall("XamUserGetName", fmt::format("idx=0 -> \"{}\"", name));
  ctx.r3.s64 = kXESuccess;
}

// X_USER_SIGNIN_INFO: 40 bytes, big-endian fields, ANSI name - layout copied
// from the runtime's X_USER_SIGNIN_INFO (xam_user.cpp).
//   0x00 u64 xuid | 0x08 u32 unk08 | 0x0C u32 signin_state
//   0x10 u32 unk10 | 0x14 u32 unk14 | 0x18 char name[16]
constexpr uint32_t kSigninInfoSize = 40;
constexpr uint32_t kSigninInfoXuid = 0x00;
constexpr uint32_t kSigninInfoState = 0x0C;
constexpr uint32_t kSigninInfoName = 0x18;

// XamUserGetSigninInfo(user_index, flags, info).
// r3 = user index, r4 = flags (unused by the runtime), r5 = info pointer.
// Mirrors XamUserGetSigninInfo_entry: zeroes the struct before the user
// check, so a bad user index still receives a zeroed struct.
void ShimGetSigninInfo(PPCContext& ctx, uint8_t* base) {
  const uint32_t user_index = static_cast<uint32_t>(ctx.r3.u64);
  const uint32_t flags = static_cast<uint32_t>(ctx.r4.u64);
  const uint32_t info = static_cast<uint32_t>(ctx.r5.u64);

  (void)flags;
  if (info == 0) {
    ctx.r3.s64 = kXEInvalidArg;
    return;
  }

  std::memset(GuestPtr(base, info), 0, kSigninInfoSize);
  if (user_index != 0) {
    ctx.r3.s64 = kXENoSuchUser;
    return;
  }

  const sfxt::Profile profile = sfxt::GetProfile();
  StoreU64(base, info + kSigninInfoXuid, profile.xuid);
  StoreU32(base, info + kSigninInfoState, profile.signed_in ? 1u : 0u);
  CopyNameTruncating(base, info + kSigninInfoName,
                     profile.signed_in ? profile.name : std::string(), 16);
  LogShimCall("XamUserGetSigninInfo",
              fmt::format("idx=0 state={} name=\"{}\"",
                          profile.signed_in ? 1 : 0,
                          profile.signed_in ? profile.name : ""));
  ctx.r3.s64 = kXESuccess;
}

}  // namespace

// Strong overrides of the runtime's import implementations. The generated
// code calls these exact extern "C" symbols; defining them here wins the link
// over rexruntime's import stubs (see file-top comment).
extern "C" void __imp__XamUserGetSigninState(PPCContext& __restrict ctx,
                                             uint8_t* base) {
  ShimGetSigninState(ctx, base);
}

extern "C" void __imp__XamUserGetXUID(PPCContext& __restrict ctx,
                                      uint8_t* base) {
  ShimGetXUID(ctx, base);
}

extern "C" void __imp__XamUserGetName(PPCContext& __restrict ctx,
                                      uint8_t* base) {
  ShimGetName(ctx, base);
}

extern "C" void __imp__XamUserGetSigninInfo(PPCContext& __restrict ctx,
                                            uint8_t* base) {
  ShimGetSigninInfo(ctx, base);
}
