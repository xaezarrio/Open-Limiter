#include <ntddk.h>
#include <wdmsec.h>
#pragma warning(push)
#pragma warning(disable:4201)
#include <ndis.h>
#include <fwpsk.h>
#pragma warning(pop)

#include "public.h"

static const GUID OPENLIMITER_DEVICE_CLASS_GUID =
    { 0xf0922f5c, 0xb7fb, 0x4ca1, { 0x9f, 0x19, 0xe5, 0x2f, 0x0e, 0x9c, 0xbd, 0xb7 } };
static const GUID OPENLIMITER_CALLOUT_FLOW_V4_GUID =
    { 0x353e9d70, 0x8ad0, 0x4475, { 0x91, 0x7d, 0x0b, 0x9a, 0x57, 0xb8, 0x22, 0xf6 } };
static const GUID OPENLIMITER_CALLOUT_FLOW_V6_GUID =
    { 0xc3610b17, 0x8873, 0x47ea, { 0x8d, 0xb2, 0x4d, 0x77, 0x2b, 0x5e, 0xed, 0x13 } };

static PDEVICE_OBJECT gDeviceObject;
static UINT32 gCalloutIdV4;
static UINT32 gCalloutIdV6;
static volatile LONG64 gClassificationCount;
static KSPIN_LOCK gFlowEventLock;
static ULONG gFlowEventCount;
static ULONG gFlowEventWriteIndex;
static OPENLIMITER_WFP_FLOW_EVENT gFlowEvents[OPENLIMITER_WFP_MAX_FLOW_EVENTS];

#if DBG
#define OPENLIMITER_DEVICE_SDDL SDDL_DEVOBJ_SYS_ALL_ADM_ALL
#else
#define OPENLIMITER_DEVICE_SDDL SDDL_DEVOBJ_SYS_ALL
#endif

DRIVER_INITIALIZE DriverEntry;
DRIVER_UNLOAD OpenLimiterUnload;

_Dispatch_type_(IRP_MJ_CREATE)
_Dispatch_type_(IRP_MJ_CLOSE)
DRIVER_DISPATCH OpenLimiterCreateClose;

_Dispatch_type_(IRP_MJ_DEVICE_CONTROL)
DRIVER_DISPATCH OpenLimiterDeviceControl;

static VOID NTAPI OpenLimiterClassify(
    _In_ const FWPS_INCOMING_VALUES0* inFixedValues,
    _In_ const FWPS_INCOMING_METADATA_VALUES0* inMetaValues,
    _Inout_opt_ VOID* layerData,
    _In_ const FWPS_FILTER0* filter,
    _In_ UINT64 flowContext,
    _Inout_ FWPS_CLASSIFY_OUT0* classifyOut);

static NTSTATUS NTAPI OpenLimiterNotify(
    _In_ FWPS_CALLOUT_NOTIFY_TYPE notifyType,
    _In_ const GUID* filterKey,
    _Inout_ const FWPS_FILTER0* filter);

static VOID NTAPI OpenLimiterFlowDelete(
    _In_ UINT16 layerId,
    _In_ UINT32 calloutId,
    _In_ UINT64 flowContext);

static NTSTATUS OpenLimiterRegisterCallout(
    _In_ const GUID* calloutKey,
    _Out_ UINT32* calloutId);

static VOID OpenLimiterUnregisterCallouts(VOID);
static UINT64 OpenLimiterHashApplicationId(_In_opt_ const FWP_BYTE_BLOB* applicationId);
static VOID OpenLimiterSetIpv4Address(_Out_writes_(16) UINT8* target, _In_ UINT32 address);
static VOID OpenLimiterRecordFlowEvent(
    _In_ const FWPS_INCOMING_VALUES0* inFixedValues,
    _In_ const FWPS_INCOMING_METADATA_VALUES0* inMetaValues);
_IRQL_requires_max_(APC_LEVEL)
static VOID OpenLimiterCopyFlowEvents(
    _Out_ POPENLIMITER_WFP_FLOW_EVENT_BATCH batch);

#ifdef ALLOC_PRAGMA
#pragma alloc_text(INIT, DriverEntry)
#pragma alloc_text(PAGE, OpenLimiterUnload)
#pragma alloc_text(PAGE, OpenLimiterCreateClose)
#pragma alloc_text(PAGE, OpenLimiterDeviceControl)
#pragma alloc_text(PAGE, OpenLimiterRegisterCallout)
#pragma alloc_text(PAGE, OpenLimiterUnregisterCallouts)
#endif

_Use_decl_annotations_
NTSTATUS
DriverEntry(
    PDRIVER_OBJECT DriverObject,
    PUNICODE_STRING RegistryPath)
{
    NTSTATUS status;
    UNICODE_STRING deviceName;
    UNICODE_STRING symbolicLinkName;

    UNREFERENCED_PARAMETER(RegistryPath);
    KeInitializeSpinLock(&gFlowEventLock);

    RtlInitUnicodeString(&deviceName, OPENLIMITER_WFP_DEVICE_NAME);
    status = IoCreateDeviceSecure(
        DriverObject,
        0,
        &deviceName,
        FILE_DEVICE_NETWORK,
        FILE_DEVICE_SECURE_OPEN,
        FALSE,
        &OPENLIMITER_DEVICE_SDDL,
        &OPENLIMITER_DEVICE_CLASS_GUID,
        &gDeviceObject);
    if (!NT_SUCCESS(status)) {
        return status;
    }

    DriverObject->MajorFunction[IRP_MJ_CREATE] = OpenLimiterCreateClose;
    DriverObject->MajorFunction[IRP_MJ_CLOSE] = OpenLimiterCreateClose;
    DriverObject->MajorFunction[IRP_MJ_DEVICE_CONTROL] = OpenLimiterDeviceControl;
    DriverObject->DriverUnload = OpenLimiterUnload;

    RtlInitUnicodeString(&symbolicLinkName, OPENLIMITER_WFP_DOS_DEVICE_NAME);
    status = IoCreateSymbolicLink(&symbolicLinkName, &deviceName);
    if (!NT_SUCCESS(status)) {
        IoDeleteDevice(gDeviceObject);
        gDeviceObject = NULL;
        return status;
    }

    status = OpenLimiterRegisterCallout(&OPENLIMITER_CALLOUT_FLOW_V4_GUID, &gCalloutIdV4);
    if (NT_SUCCESS(status)) {
        status = OpenLimiterRegisterCallout(&OPENLIMITER_CALLOUT_FLOW_V6_GUID, &gCalloutIdV6);
    }

    if (!NT_SUCCESS(status)) {
        OpenLimiterUnregisterCallouts();
        IoDeleteSymbolicLink(&symbolicLinkName);
        IoDeleteDevice(gDeviceObject);
        gDeviceObject = NULL;
        return status;
    }

    gDeviceObject->Flags &= ~DO_DEVICE_INITIALIZING;
    return STATUS_SUCCESS;
}

_Use_decl_annotations_
VOID
OpenLimiterUnload(PDRIVER_OBJECT DriverObject)
{
    UNICODE_STRING symbolicLinkName;

    PAGED_CODE();
    OpenLimiterUnregisterCallouts();
    RtlInitUnicodeString(&symbolicLinkName, OPENLIMITER_WFP_DOS_DEVICE_NAME);
    IoDeleteSymbolicLink(&symbolicLinkName);

    if (DriverObject->DeviceObject != NULL) {
        IoDeleteDevice(DriverObject->DeviceObject);
        gDeviceObject = NULL;
    }
}

_Use_decl_annotations_
NTSTATUS
OpenLimiterCreateClose(PDEVICE_OBJECT DeviceObject, PIRP Irp)
{
    UNREFERENCED_PARAMETER(DeviceObject);
    PAGED_CODE();

    Irp->IoStatus.Status = STATUS_SUCCESS;
    Irp->IoStatus.Information = 0;
    IoCompleteRequest(Irp, IO_NO_INCREMENT);
    return STATUS_SUCCESS;
}

_Use_decl_annotations_
NTSTATUS
OpenLimiterDeviceControl(PDEVICE_OBJECT DeviceObject, PIRP Irp)
{
    NTSTATUS status = STATUS_INVALID_DEVICE_REQUEST;
    ULONG_PTR bytesReturned = 0;
    PIO_STACK_LOCATION stack;

    UNREFERENCED_PARAMETER(DeviceObject);
    PAGED_CODE();

    stack = IoGetCurrentIrpStackLocation(Irp);
    if (stack->Parameters.DeviceIoControl.IoControlCode == IOCTL_OPENLIMITER_WFP_GET_CAPABILITIES) {
        if (stack->Parameters.DeviceIoControl.OutputBufferLength < sizeof(OPENLIMITER_WFP_CAPABILITIES) ||
            Irp->AssociatedIrp.SystemBuffer == NULL) {
            status = STATUS_BUFFER_TOO_SMALL;
        }
        else {
            POPENLIMITER_WFP_CAPABILITIES capabilities =
                (POPENLIMITER_WFP_CAPABILITIES)Irp->AssociatedIrp.SystemBuffer;

            RtlZeroMemory(capabilities, sizeof(*capabilities));
            capabilities->Size = sizeof(*capabilities);
            capabilities->ApiMajor = OPENLIMITER_WFP_API_MAJOR;
            capabilities->ApiMinor = OPENLIMITER_WFP_API_MINOR;
            capabilities->Flags =
                OPENLIMITER_WFP_CAPABILITY_PASS_THROUGH |
                OPENLIMITER_WFP_CAPABILITY_FLOW_EVENTS |
                OPENLIMITER_WFP_CAPABILITY_FLOW_IDENTITY |
                OPENLIMITER_WFP_CAPABILITY_FLOW_PROTOCOL_DIRECTION |
                OPENLIMITER_WFP_CAPABILITY_FLOW_ENDPOINTS;
            capabilities->RegisteredCallouts =
                (gCalloutIdV4 != 0 ? 1U : 0U) +
                (gCalloutIdV6 != 0 ? 1U : 0U);
            capabilities->ClassificationCount = (UINT64)InterlockedCompareExchange64(
                &gClassificationCount,
                0,
                0);
            bytesReturned = sizeof(*capabilities);
            status = STATUS_SUCCESS;
        }
    }
    else if (stack->Parameters.DeviceIoControl.IoControlCode == IOCTL_OPENLIMITER_WFP_GET_FLOW_EVENTS) {
        if (stack->Parameters.DeviceIoControl.OutputBufferLength < sizeof(OPENLIMITER_WFP_FLOW_EVENT_BATCH) ||
            Irp->AssociatedIrp.SystemBuffer == NULL) {
            status = STATUS_BUFFER_TOO_SMALL;
        }
        else {
            POPENLIMITER_WFP_FLOW_EVENT_BATCH batch =
                (POPENLIMITER_WFP_FLOW_EVENT_BATCH)Irp->AssociatedIrp.SystemBuffer;

            OpenLimiterCopyFlowEvents(batch);
            bytesReturned = sizeof(*batch);
            status = STATUS_SUCCESS;
        }
    }

    Irp->IoStatus.Status = status;
    Irp->IoStatus.Information = bytesReturned;
    IoCompleteRequest(Irp, IO_NO_INCREMENT);
    return status;
}

static VOID NTAPI
OpenLimiterClassify(
    _In_ const FWPS_INCOMING_VALUES0* inFixedValues,
    _In_ const FWPS_INCOMING_METADATA_VALUES0* inMetaValues,
    _Inout_opt_ VOID* layerData,
    _In_ const FWPS_FILTER0* filter,
    _In_ UINT64 flowContext,
    _Inout_ FWPS_CLASSIFY_OUT0* classifyOut)
{
    UNREFERENCED_PARAMETER(inFixedValues);
    UNREFERENCED_PARAMETER(inMetaValues);
    UNREFERENCED_PARAMETER(layerData);
    UNREFERENCED_PARAMETER(filter);
    UNREFERENCED_PARAMETER(flowContext);

    OpenLimiterRecordFlowEvent(inFixedValues, inMetaValues);
    if ((classifyOut->rights & FWPS_RIGHT_ACTION_WRITE) != 0) {
        classifyOut->actionType = FWP_ACTION_CONTINUE;
    }
}

static UINT64
OpenLimiterHashApplicationId(_In_opt_ const FWP_BYTE_BLOB* applicationId)
{
    const UINT64 offsetBasis = 14695981039346656037ULL;
    const UINT64 prime = 1099511628211ULL;
    UINT64 hash = offsetBasis;
    UINT32 byteCount;

    if (applicationId == NULL || applicationId->data == NULL) {
        return 0;
    }

    byteCount = min(applicationId->size, 4096U);
    for (UINT32 index = 0; index < byteCount; index++) {
        hash ^= applicationId->data[index];
        hash *= prime;
    }

    hash ^= applicationId->size;
    hash *= prime;
    return hash;
}

static VOID
OpenLimiterSetIpv4Address(_Out_writes_(16) UINT8* target, _In_ UINT32 address)
{
    RtlZeroMemory(target, 16);
    target[0] = (UINT8)(address >> 24);
    target[1] = (UINT8)(address >> 16);
    target[2] = (UINT8)(address >> 8);
    target[3] = (UINT8)address;
}

static VOID
OpenLimiterRecordFlowEvent(
    _In_ const FWPS_INCOMING_VALUES0* inFixedValues,
    _In_ const FWPS_INCOMING_METADATA_VALUES0* inMetaValues)
{
    const FWP_BYTE_BLOB* applicationId = NULL;
    OPENLIMITER_WFP_FLOW_EVENT flowEvent = { 0 };
    KIRQL oldIrql;

    if (inFixedValues->layerId == FWPS_LAYER_ALE_FLOW_ESTABLISHED_V4) {
        const FWP_VALUE0* appIdValue = &inFixedValues->incomingValue[
            FWPS_FIELD_ALE_FLOW_ESTABLISHED_V4_ALE_APP_ID].value;
        const FWP_VALUE0* protocolValue = &inFixedValues->incomingValue[
            FWPS_FIELD_ALE_FLOW_ESTABLISHED_V4_IP_PROTOCOL].value;
        const FWP_VALUE0* directionValue = &inFixedValues->incomingValue[
            FWPS_FIELD_ALE_FLOW_ESTABLISHED_V4_DIRECTION].value;
        const FWP_VALUE0* localAddressValue = &inFixedValues->incomingValue[
            FWPS_FIELD_ALE_FLOW_ESTABLISHED_V4_IP_LOCAL_ADDRESS].value;
        const FWP_VALUE0* remoteAddressValue = &inFixedValues->incomingValue[
            FWPS_FIELD_ALE_FLOW_ESTABLISHED_V4_IP_REMOTE_ADDRESS].value;
        const FWP_VALUE0* localPortValue = &inFixedValues->incomingValue[
            FWPS_FIELD_ALE_FLOW_ESTABLISHED_V4_IP_LOCAL_PORT].value;
        const FWP_VALUE0* remotePortValue = &inFixedValues->incomingValue[
            FWPS_FIELD_ALE_FLOW_ESTABLISHED_V4_IP_REMOTE_PORT].value;

        if (appIdValue->type == FWP_BYTE_BLOB_TYPE) {
            applicationId = appIdValue->byteBlob;
        }
        if (protocolValue->type == FWP_UINT8) {
            flowEvent.IpProtocol = protocolValue->uint8;
        }
        if (directionValue->type == FWP_UINT32) {
            flowEvent.Direction = (UINT8)directionValue->uint32;
        }
        if (localAddressValue->type == FWP_UINT32) {
            OpenLimiterSetIpv4Address(flowEvent.LocalAddress, localAddressValue->uint32);
        }
        if (remoteAddressValue->type == FWP_UINT32) {
            OpenLimiterSetIpv4Address(flowEvent.RemoteAddress, remoteAddressValue->uint32);
        }
        if ((flowEvent.IpProtocol == IPPROTO_TCP || flowEvent.IpProtocol == IPPROTO_UDP) &&
            localPortValue->type == FWP_UINT16 && remotePortValue->type == FWP_UINT16) {
            flowEvent.LocalPort = localPortValue->uint16;
            flowEvent.RemotePort = remotePortValue->uint16;
        }
    }
    else if (inFixedValues->layerId == FWPS_LAYER_ALE_FLOW_ESTABLISHED_V6) {
        const FWP_VALUE0* appIdValue = &inFixedValues->incomingValue[
            FWPS_FIELD_ALE_FLOW_ESTABLISHED_V6_ALE_APP_ID].value;
        const FWP_VALUE0* protocolValue = &inFixedValues->incomingValue[
            FWPS_FIELD_ALE_FLOW_ESTABLISHED_V6_IP_PROTOCOL].value;
        const FWP_VALUE0* directionValue = &inFixedValues->incomingValue[
            FWPS_FIELD_ALE_FLOW_ESTABLISHED_V6_DIRECTION].value;
        const FWP_VALUE0* localAddressValue = &inFixedValues->incomingValue[
            FWPS_FIELD_ALE_FLOW_ESTABLISHED_V6_IP_LOCAL_ADDRESS].value;
        const FWP_VALUE0* remoteAddressValue = &inFixedValues->incomingValue[
            FWPS_FIELD_ALE_FLOW_ESTABLISHED_V6_IP_REMOTE_ADDRESS].value;
        const FWP_VALUE0* localPortValue = &inFixedValues->incomingValue[
            FWPS_FIELD_ALE_FLOW_ESTABLISHED_V6_IP_LOCAL_PORT].value;
        const FWP_VALUE0* remotePortValue = &inFixedValues->incomingValue[
            FWPS_FIELD_ALE_FLOW_ESTABLISHED_V6_IP_REMOTE_PORT].value;

        if (appIdValue->type == FWP_BYTE_BLOB_TYPE) {
            applicationId = appIdValue->byteBlob;
        }
        if (protocolValue->type == FWP_UINT8) {
            flowEvent.IpProtocol = protocolValue->uint8;
        }
        if (directionValue->type == FWP_UINT32) {
            flowEvent.Direction = (UINT8)directionValue->uint32;
        }
        if (localAddressValue->type == FWP_BYTE_ARRAY16_TYPE &&
            localAddressValue->byteArray16 != NULL) {
            RtlCopyMemory(flowEvent.LocalAddress, localAddressValue->byteArray16->byteArray16, 16);
        }
        if (remoteAddressValue->type == FWP_BYTE_ARRAY16_TYPE &&
            remoteAddressValue->byteArray16 != NULL) {
            RtlCopyMemory(flowEvent.RemoteAddress, remoteAddressValue->byteArray16->byteArray16, 16);
        }
        if ((flowEvent.IpProtocol == IPPROTO_TCP || flowEvent.IpProtocol == IPPROTO_UDP) &&
            localPortValue->type == FWP_UINT16 && remotePortValue->type == FWP_UINT16) {
            flowEvent.LocalPort = localPortValue->uint16;
            flowEvent.RemotePort = remotePortValue->uint16;
        }
    }

    flowEvent.Sequence = (UINT64)InterlockedIncrement64(&gClassificationCount);
    flowEvent.ApplicationIdHash = OpenLimiterHashApplicationId(applicationId);
    flowEvent.IpVersion = inFixedValues->layerId == FWPS_LAYER_ALE_FLOW_ESTABLISHED_V6 ? 6U : 4U;
    if (FWPS_IS_METADATA_FIELD_PRESENT(inMetaValues, FWPS_METADATA_FIELD_PROCESS_ID)) {
        flowEvent.ProcessId = inMetaValues->processId;
    }

    KeAcquireSpinLock(&gFlowEventLock, &oldIrql);
    gFlowEvents[gFlowEventWriteIndex] = flowEvent;
    gFlowEventWriteIndex = (gFlowEventWriteIndex + 1) % OPENLIMITER_WFP_MAX_FLOW_EVENTS;
    if (gFlowEventCount < OPENLIMITER_WFP_MAX_FLOW_EVENTS) {
        gFlowEventCount++;
    }
    KeReleaseSpinLock(&gFlowEventLock, oldIrql);
}

_IRQL_requires_max_(APC_LEVEL)
static VOID
OpenLimiterCopyFlowEvents(
    _Out_ POPENLIMITER_WFP_FLOW_EVENT_BATCH batch)
{
    KIRQL oldIrql;
    ULONG index;

    RtlZeroMemory(batch, sizeof(*batch));
    batch->Size = sizeof(*batch);
    KeAcquireSpinLock(&gFlowEventLock, &oldIrql);
    batch->Count = gFlowEventCount;
    index = (gFlowEventWriteIndex + OPENLIMITER_WFP_MAX_FLOW_EVENTS - gFlowEventCount) %
        OPENLIMITER_WFP_MAX_FLOW_EVENTS;
    for (ULONG eventIndex = 0; eventIndex < gFlowEventCount; eventIndex++) {
        batch->Events[eventIndex] = gFlowEvents[index];
        batch->LatestSequence = batch->Events[eventIndex].Sequence;
        index = (index + 1) % OPENLIMITER_WFP_MAX_FLOW_EVENTS;
    }
    KeReleaseSpinLock(&gFlowEventLock, oldIrql);
}

static NTSTATUS NTAPI
OpenLimiterNotify(
    _In_ FWPS_CALLOUT_NOTIFY_TYPE notifyType,
    _In_ const GUID* filterKey,
    _Inout_ const FWPS_FILTER0* filter)
{
    UNREFERENCED_PARAMETER(notifyType);
    UNREFERENCED_PARAMETER(filterKey);
    UNREFERENCED_PARAMETER(filter);
    return STATUS_SUCCESS;
}

static VOID NTAPI
OpenLimiterFlowDelete(
    _In_ UINT16 layerId,
    _In_ UINT32 calloutId,
    _In_ UINT64 flowContext)
{
    UNREFERENCED_PARAMETER(layerId);
    UNREFERENCED_PARAMETER(calloutId);
    UNREFERENCED_PARAMETER(flowContext);
}

static NTSTATUS
OpenLimiterRegisterCallout(
    _In_ const GUID* calloutKey,
    _Out_ UINT32* calloutId)
{
    FWPS_CALLOUT0 callout;

    PAGED_CODE();
    RtlZeroMemory(&callout, sizeof(callout));
    callout.calloutKey = *calloutKey;
    callout.classifyFn = OpenLimiterClassify;
    callout.notifyFn = OpenLimiterNotify;
    callout.flowDeleteFn = OpenLimiterFlowDelete;
    return FwpsCalloutRegister0(gDeviceObject, &callout, calloutId);
}

static VOID
OpenLimiterUnregisterCallouts(VOID)
{
    PAGED_CODE();

    if (gCalloutIdV6 != 0) {
        FwpsCalloutUnregisterById0(gCalloutIdV6);
        gCalloutIdV6 = 0;
    }

    if (gCalloutIdV4 != 0) {
        FwpsCalloutUnregisterById0(gCalloutIdV4);
        gCalloutIdV4 = 0;
    }
}
