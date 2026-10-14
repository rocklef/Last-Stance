#include "RuinBreakWall.h"
#include "Components/SceneComponent.h"
#include "Components/StaticMeshComponent.h"
#include "Engine/StaticMesh.h"
ARuinBreakWall::ARuinBreakWall()
{
    USceneComponent* Root = CreateDefaultSubobject<USceneComponent>(TEXT("Root")); RootComponent = Root; UStaticMesh* Cube = LoadObject<UStaticMesh>(nullptr, TEXT("/Engine/BasicShapes/Cube.Cube"));
    for (int32 Row = 0; Row < 5; ++Row) for (int32 Col = 0; Col < 7; ++Col) { UStaticMeshComponent* Chunk = CreateDefaultSubobject<UStaticMeshComponent>(*FString::Printf(TEXT("Chunk_%d_%d"), Row, Col)); Chunk->SetupAttachment(Root); Chunk->SetStaticMesh(Cube); Chunk->SetRelativeLocation(FVector(0, (Col - 3) * 360.f, Row * 330.f + 120.f)); Chunk->SetRelativeScale3D(FVector(.5f, 1.55f, .45f)); Chunk->SetCollisionProfileName(TEXT("PhysicsActor")); Chunk->SetSimulatePhysics(false); Chunks.Add(Chunk); }
}
void ARuinBreakWall::Shatter(const FVector& Impulse) { if (bShattered) return; bShattered = true; for (UStaticMeshComponent* Chunk : Chunks) { Chunk->SetSimulatePhysics(true); Chunk->AddImpulse(Impulse * FMath::FRandRange(.65f, 1.35f)); Chunk->AddAngularImpulseInDegrees(FVector(FMath::FRandRange(-100000.f, 100000.f), FMath::FRandRange(-100000.f, 100000.f), FMath::FRandRange(-100000.f, 100000.f))); } }
