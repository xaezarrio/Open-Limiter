#include <windows.h>
#include <fwpmu.h>
#include <rpc.h>
#include <cstdio>
#include <cwchar>

#include "public.h"

static_assert(sizeof(FWP_VALUE0) == 16, "Unexpected x64 FWP_VALUE0 layout");
static_assert(sizeof(FWPM_ACTION0) == 20, "Unexpected x64 FWPM_ACTION0 layout");
static_assert(sizeof(FWPM_SESSION0) == 72, "Unexpected x64 FWPM_SESSION0 layout");
static_assert(sizeof(FWPM_PROVIDER0) == 64, "Unexpected x64 FWPM_PROVIDER0 layout");
static_assert(sizeof(FWPM_SUBLAYER0) == 72, "Unexpected x64 FWPM_SUBLAYER0 layout");
static_assert(sizeof(FWPM_CALLOUT0) == 88, "Unexpected x64 FWPM_CALLOUT0 layout");
static_assert(sizeof(FWPM_FILTER0) == 200, "Unexpected x64 FWPM_FILTER0 layout");

static const GUID OPENLIMITER_PROVIDER_GUID =
    { 0xa95745a6, 0x70cf, 0x42fc, { 0xae, 0xe8, 0x47, 0xd7, 0x78, 0xed, 0x1f, 0x45 } };
static const GUID OPENLIMITER_SUBLAYER_GUID =
    { 0x098810b7, 0xa34b, 0x43cc, { 0xbf, 0xf1, 0xc9, 0xe0, 0x49, 0x60, 0x17, 0x52 } };
static const GUID OPENLIMITER_CALLOUT_FLOW_V4_GUID =
    { 0x353e9d70, 0x8ad0, 0x4475, { 0x91, 0x7d, 0x0b, 0x9a, 0x57, 0xb8, 0x22, 0xf6 } };
static const GUID OPENLIMITER_CALLOUT_FLOW_V6_GUID =
    { 0xc3610b17, 0x8873, 0x47ea, { 0x8d, 0xb2, 0x4d, 0x77, 0x2b, 0x5e, 0xed, 0x13 } };
static const GUID OPENLIMITER_FILTER_FLOW_V4_GUID =
    { 0x91c33d5b, 0x4208, 0x42b1, { 0x8d, 0x1e, 0xd3, 0x6c, 0x6b, 0xbd, 0xce, 0xf1 } };
static const GUID OPENLIMITER_FILTER_FLOW_V6_GUID =
    { 0xa6d1b260, 0x6d7d, 0x48db, { 0x8e, 0x26, 0x1e, 0xea, 0xf1, 0x60, 0x48, 0x8d } };

static HANDLE gStopEvent;

static BOOL WINAPI ConsoleHandler(DWORD controlType)
{
    if (controlType == CTRL_C_EVENT || controlType == CTRL_BREAK_EVENT || controlType == CTRL_CLOSE_EVENT) {
        SetEvent(gStopEvent);
        return TRUE;
    }

    return FALSE;
}

static DWORD CheckDriver(OPENLIMITER_WFP_CAPABILITIES* capabilities)
{
    HANDLE device = CreateFileW(
        L"\\\\.\\OpenLimiterWfp",
        GENERIC_READ,
        0,
        nullptr,
        OPEN_EXISTING,
        FILE_ATTRIBUTE_NORMAL,
        nullptr);
    if (device == INVALID_HANDLE_VALUE) {
        return GetLastError();
    }

    DWORD bytesReturned = 0;
    const BOOL succeeded = DeviceIoControl(
        device,
        IOCTL_OPENLIMITER_WFP_GET_CAPABILITIES,
        nullptr,
        0,
        capabilities,
        sizeof(*capabilities),
        &bytesReturned,
        nullptr);
    const DWORD result = succeeded ? ERROR_SUCCESS : GetLastError();
    CloseHandle(device);

    if (result == ERROR_SUCCESS &&
        (bytesReturned != sizeof(*capabilities) ||
         capabilities->Size != sizeof(*capabilities) ||
         capabilities->ApiMajor != OPENLIMITER_WFP_API_MAJOR ||
         capabilities->ApiMinor < OPENLIMITER_WFP_API_MINOR ||
         (capabilities->Flags &
             (OPENLIMITER_WFP_CAPABILITY_PASS_THROUGH |
              OPENLIMITER_WFP_CAPABILITY_FLOW_EVENTS |
              OPENLIMITER_WFP_CAPABILITY_FLOW_IDENTITY |
              OPENLIMITER_WFP_CAPABILITY_FLOW_PROTOCOL_DIRECTION |
              OPENLIMITER_WFP_CAPABILITY_FLOW_ENDPOINTS)) !=
             (OPENLIMITER_WFP_CAPABILITY_PASS_THROUGH |
              OPENLIMITER_WFP_CAPABILITY_FLOW_EVENTS |
              OPENLIMITER_WFP_CAPABILITY_FLOW_IDENTITY |
              OPENLIMITER_WFP_CAPABILITY_FLOW_PROTOCOL_DIRECTION |
              OPENLIMITER_WFP_CAPABILITY_FLOW_ENDPOINTS) ||
         capabilities->RegisteredCallouts != 2)) {
        return ERROR_REVISION_MISMATCH;
    }

    return result;
}

static DWORD ReadFlowEvents(OPENLIMITER_WFP_FLOW_EVENT_BATCH* batch)
{
    HANDLE device = CreateFileW(
        L"\\\\.\\OpenLimiterWfp",
        GENERIC_READ,
        0,
        nullptr,
        OPEN_EXISTING,
        FILE_ATTRIBUTE_NORMAL,
        nullptr);
    if (device == INVALID_HANDLE_VALUE) {
        return GetLastError();
    }

    DWORD bytesReturned = 0;
    const BOOL succeeded = DeviceIoControl(
        device,
        IOCTL_OPENLIMITER_WFP_GET_FLOW_EVENTS,
        nullptr,
        0,
        batch,
        sizeof(*batch),
        &bytesReturned,
        nullptr);
    const DWORD result = succeeded ? ERROR_SUCCESS : GetLastError();
    CloseHandle(device);
    if (result == ERROR_SUCCESS && (bytesReturned != sizeof(*batch) || batch->Size != sizeof(*batch))) {
        return ERROR_REVISION_MISMATCH;
    }

    return result;
}

static DWORD AddCallout(
    HANDLE engine,
    const GUID& calloutKey,
    const GUID& layerKey,
    wchar_t* name)
{
    FWPM_CALLOUT0 callout = {};
    callout.calloutKey = calloutKey;
    callout.displayData.name = name;
    callout.providerKey = const_cast<GUID*>(&OPENLIMITER_PROVIDER_GUID);
    callout.applicableLayer = layerKey;
    return FwpmCalloutAdd0(engine, &callout, nullptr, nullptr);
}

static DWORD AddFilter(
    HANDLE engine,
    const GUID& filterKey,
    const GUID& calloutKey,
    const GUID& layerKey,
    wchar_t* name)
{
    FWPM_FILTER0 filter = {};
    filter.filterKey = filterKey;
    filter.displayData.name = name;
    filter.providerKey = const_cast<GUID*>(&OPENLIMITER_PROVIDER_GUID);
    filter.layerKey = layerKey;
    filter.subLayerKey = OPENLIMITER_SUBLAYER_GUID;
    filter.weight.type = FWP_EMPTY;
    filter.action.type = FWP_ACTION_CALLOUT_INSPECTION;
    filter.action.calloutKey = calloutKey;
    return FwpmFilterAdd0(engine, &filter, nullptr, nullptr);
}

static DWORD OpenPolicySession(HANDLE* engine)
{
    wchar_t sessionName[] = L"OpenLimiter WFP test session";
    wchar_t providerName[] = L"OpenLimiter test provider";
    wchar_t subLayerName[] = L"OpenLimiter test sublayer";
    wchar_t calloutV4Name[] = L"OpenLimiter established-flow IPv4 callout";
    wchar_t calloutV6Name[] = L"OpenLimiter established-flow IPv6 callout";
    wchar_t filterV4Name[] = L"OpenLimiter established-flow IPv4 test filter";
    wchar_t filterV6Name[] = L"OpenLimiter established-flow IPv6 test filter";

    FWPM_SESSION0 session = {};
    session.displayData.name = sessionName;
    session.flags = FWPM_SESSION_FLAG_DYNAMIC;
    session.txnWaitTimeoutInMSec = 5000;

    DWORD result = FwpmEngineOpen0(nullptr, RPC_C_AUTHN_WINNT, nullptr, &session, engine);
    if (result != ERROR_SUCCESS) {
        return result;
    }

    result = FwpmTransactionBegin0(*engine, 0);
    if (result != ERROR_SUCCESS) {
        FwpmEngineClose0(*engine);
        *engine = nullptr;
        return result;
    }

    FWPM_PROVIDER0 provider = {};
    provider.providerKey = OPENLIMITER_PROVIDER_GUID;
    provider.displayData.name = providerName;
    result = FwpmProviderAdd0(*engine, &provider, nullptr);

    if (result == ERROR_SUCCESS) {
        FWPM_SUBLAYER0 subLayer = {};
        subLayer.subLayerKey = OPENLIMITER_SUBLAYER_GUID;
        subLayer.displayData.name = subLayerName;
        subLayer.providerKey = const_cast<GUID*>(&OPENLIMITER_PROVIDER_GUID);
        subLayer.weight = 0x100;
        result = FwpmSubLayerAdd0(*engine, &subLayer, nullptr);
    }

    if (result == ERROR_SUCCESS) {
        result = AddCallout(*engine, OPENLIMITER_CALLOUT_FLOW_V4_GUID, FWPM_LAYER_ALE_FLOW_ESTABLISHED_V4, calloutV4Name);
    }
    if (result == ERROR_SUCCESS) {
        result = AddCallout(*engine, OPENLIMITER_CALLOUT_FLOW_V6_GUID, FWPM_LAYER_ALE_FLOW_ESTABLISHED_V6, calloutV6Name);
    }
    if (result == ERROR_SUCCESS) {
        result = AddFilter(*engine, OPENLIMITER_FILTER_FLOW_V4_GUID, OPENLIMITER_CALLOUT_FLOW_V4_GUID, FWPM_LAYER_ALE_FLOW_ESTABLISHED_V4, filterV4Name);
    }
    if (result == ERROR_SUCCESS) {
        result = AddFilter(*engine, OPENLIMITER_FILTER_FLOW_V6_GUID, OPENLIMITER_CALLOUT_FLOW_V6_GUID, FWPM_LAYER_ALE_FLOW_ESTABLISHED_V6, filterV6Name);
    }

    if (result == ERROR_SUCCESS) {
        result = FwpmTransactionCommit0(*engine);
    }
    else {
        FwpmTransactionAbort0(*engine);
    }

    if (result != ERROR_SUCCESS) {
        FwpmEngineClose0(*engine);
        *engine = nullptr;
    }

    return result;
}

int wmain()
{
    OPENLIMITER_WFP_CAPABILITIES capabilities = {};
    DWORD result = CheckDriver(&capabilities);
    if (result != ERROR_SUCCESS) {
        std::fwprintf(stderr, L"OpenLimiter WFP driver capability check failed: 0x%08lX\n", result);
        return 1;
    }

    HANDLE engine = nullptr;
    result = OpenPolicySession(&engine);
    if (result != ERROR_SUCCESS) {
        std::fwprintf(stderr, L"Could not create the dynamic WFP policy session: 0x%08lX\n", result);
        return 2;
    }

    gStopEvent = CreateEventW(nullptr, TRUE, FALSE, nullptr);
    if (gStopEvent == nullptr || !SetConsoleCtrlHandler(ConsoleHandler, TRUE)) {
        result = GetLastError();
        if (gStopEvent != nullptr) {
            CloseHandle(gStopEvent);
        }
        FwpmEngineClose0(engine);
        std::fwprintf(stderr, L"Could not initialize shutdown handling: 0x%08lX\n", result);
        return 3;
    }

    std::wprintf(
        L"Dynamic pass-through filters are active. API %u.%u, callouts %u. Press Ctrl+C to stop.\n",
        capabilities.ApiMajor,
        capabilities.ApiMinor,
        capabilities.RegisteredCallouts);
    UINT64 lastSequence = 0;
    while (WaitForSingleObject(gStopEvent, 1000) == WAIT_TIMEOUT) {
        OPENLIMITER_WFP_FLOW_EVENT_BATCH batch = {};
        result = ReadFlowEvents(&batch);
        if (result != ERROR_SUCCESS) {
            std::fwprintf(stderr, L"Could not read flow events: 0x%08lX\n", result);
            break;
        }

        for (UINT32 index = 0; index < batch.Count; index++) {
            const OPENLIMITER_WFP_FLOW_EVENT& flowEvent = batch.Events[index];
            if (flowEvent.Sequence <= lastSequence) {
                continue;
            }

            std::wprintf(
                L"flow=%llu pid=%llu IPv%u protocol=%u direction=%u local-port=%u remote-port=%u app=%016llX\n",
                static_cast<unsigned long long>(flowEvent.Sequence),
                static_cast<unsigned long long>(flowEvent.ProcessId),
                flowEvent.IpVersion,
                flowEvent.IpProtocol,
                flowEvent.Direction,
                flowEvent.LocalPort,
                flowEvent.RemotePort,
                static_cast<unsigned long long>(flowEvent.ApplicationIdHash));
            lastSequence = flowEvent.Sequence;
        }
    }

    SetConsoleCtrlHandler(ConsoleHandler, FALSE);
    CloseHandle(gStopEvent);
    gStopEvent = nullptr;
    FwpmEngineClose0(engine);
    std::wprintf(L"Dynamic WFP policy removed.\n");
    return 0;
}
