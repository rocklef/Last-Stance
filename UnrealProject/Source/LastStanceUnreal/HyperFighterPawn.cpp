#include "HyperFighterPawn.h"
#include "HyperArenaGameMode.h"
#include "Camera/CameraComponent.h"
#include "Components/CapsuleComponent.h"
#include "Components/StaticMeshComponent.h"
#include "GameFramework/SpringArmComponent.h"
#include "Components/InputComponent.h"
#include "Engine/StaticMesh.h"
AHyperFighterPawn::AHyperFighterPawn()
{
    PrimaryActorTick.bCanEverTick = true; Capsule = CreateDefaultSubobject<UCapsuleComponent>(TEXT("Capsule")); Capsule->InitCapsuleSize(42, 94); Capsule->SetCollisionProfileName(TEXT("Pawn")); RootComponent = Capsule;
    MakePiece(TEXT("Torso"), FVector(0, 0, 105), FVector(.38f, .25f, .62f)); MakePiece(TEXT("Head"), FVector(0, 0, 185), FVector(.27f)); MakePiece(TEXT("ArmL"), FVector(0, -48, 112), FVector(.16f, .15f, .55f)); MakePiece(TEXT("ArmR"), FVector(0, 48, 112), FVector(.16f, .15f, .55f)); MakePiece(TEXT("LegL"), FVector(0, -22, 33), FVector(.17f, .17f, .55f)); MakePiece(TEXT("LegR"), FVector(0, 22, 33), FVector(.17f, .17f, .55f));
    CameraBoom = CreateDefaultSubobject<USpringArmComponent>(TEXT("CameraBoom")); CameraBoom->SetupAttachment(RootComponent); CameraBoom->TargetArmLength = 680; CameraBoom->SetRelativeRotation(FRotator(-12, 0, 0)); CameraBoom->bEnableCameraLag = true; CameraBoom->CameraLagSpeed = 4;
    Camera = CreateDefaultSubobject<UCameraComponent>(TEXT("Camera")); Camera->SetupAttachment(CameraBoom);
}
UStaticMeshComponent* AHyperFighterPawn::MakePiece(const FName Name, const FVector& Location, const FVector& Scale) { UStaticMeshComponent* Piece = CreateDefaultSubobject<UStaticMeshComponent>(Name); Piece->SetupAttachment(RootComponent); Piece->SetStaticMesh(LoadObject<UStaticMesh>(nullptr, TEXT("/Engine/BasicShapes/Cube.Cube"))); Piece->SetRelativeLocation(Location); Piece->SetRelativeScale3D(Scale); Piece->SetCollisionProfileName(TEXT("PhysicsActor")); Piece->SetSimulatePhysics(false); PhysicsPieces.Add(Piece); return Piece; }
void AHyperFighterPawn::Tick(float DeltaSeconds) { Super::Tick(DeltaSeconds); if (!bLaunching || bRagdolled) return; AddActorWorldOffset(FlightVelocity * DeltaSeconds, true); FlightVelocity.Z -= 980.f * DeltaSeconds; CameraBoom->TargetArmLength = FMath::FInterpTo(CameraBoom->TargetArmLength, 950.f, DeltaSeconds, 2.f); }
void AHyperFighterPawn::SetupPlayerInputComponent(UInputComponent* Input) { Super::SetupPlayerInputComponent(Input); Input->BindAxis(TEXT("MoveForward"), this, &AHyperFighterPawn::MoveForward); Input->BindAxis(TEXT("MoveRight"), this, &AHyperFighterPawn::MoveRight); Input->BindAction(TEXT("LightStrike"), IE_Pressed, this, &AHyperFighterPawn::LightStrike); Input->BindAction(TEXT("HeavyStrike"), IE_Pressed, this, &AHyperFighterPawn::HeavyStrike); Input->BindAction(TEXT("ManualMode"), IE_Pressed, this, &AHyperFighterPawn::ManualMode); Input->BindAction(TEXT("StoryMode"), IE_Pressed, this, &AHyperFighterPawn::StoryMode); Input->BindAction(TEXT("BossPower"), IE_Pressed, this, &AHyperFighterPawn::BossPower); }
void AHyperFighterPawn::MoveForward(float V) { if (!bLaunching && !bRagdolled) AddActorWorldOffset(GetActorForwardVector() * V * 600.f * GetWorld()->GetDeltaSeconds(), true); }
void AHyperFighterPawn::MoveRight(float V) { if (!bLaunching && !bRagdolled) AddActorWorldOffset(GetActorRightVector() * V * 600.f * GetWorld()->GetDeltaSeconds(), true); }
void AHyperFighterPawn::LightStrike() { if (AHyperArenaGameMode* M = GetWorld()->GetAuthGameMode<AHyperArenaGameMode>()) M->ManualStrike(); }
void AHyperFighterPawn::HeavyStrike() { if (AHyperArenaGameMode* M = GetWorld()->GetAuthGameMode<AHyperArenaGameMode>()) M->ManualStrike(); }
void AHyperFighterPawn::ManualMode() { if (AHyperArenaGameMode* M = GetWorld()->GetAuthGameMode<AHyperArenaGameMode>()) M->SelectMode(false); }
void AHyperFighterPawn::StoryMode() { if (AHyperArenaGameMode* M = GetWorld()->GetAuthGameMode<AHyperArenaGameMode>()) M->SelectMode(true); }
void AHyperFighterPawn::BossPower() { if (AHyperArenaGameMode* M = GetWorld()->GetAuthGameMode<AHyperArenaGameMode>()) M->TriggerBossPower(); }
void AHyperFighterPawn::StartPowerLaunch(const FVector& Direction, float Strength) { if (!bRagdolled) { bLaunching = true; FlightVelocity = Direction.GetSafeNormal() * Strength + FVector(0, 0, 1200); } }
void AHyperFighterPawn::EnterRagdoll(const FVector& Impulse) { if (bRagdolled) return; bRagdolled = true; bLaunching = false; Capsule->SetCollisionEnabled(ECollisionEnabled::NoCollision); for (UStaticMeshComponent* Piece : PhysicsPieces) { Piece->DetachFromComponent(FDetachmentTransformRules::KeepWorldTransform); Piece->SetSimulatePhysics(true); Piece->AddImpulse(Impulse * FMath::FRandRange(.75f, 1.2f)); } }
void AHyperFighterPawn::ResetFighter() { bLaunching = false; bRagdolled = false; FlightVelocity = FVector::ZeroVector; SetActorLocation(FVector(0, 0, 100)); Capsule->SetCollisionEnabled(ECollisionEnabled::QueryAndPhysics); for (UStaticMeshComponent* Piece : PhysicsPieces) { Piece->SetSimulatePhysics(false); Piece->AttachToComponent(RootComponent, FAttachmentTransformRules::KeepWorldTransform); } CameraBoom->TargetArmLength = 680; }
