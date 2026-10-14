#pragma once
#include "CoreMinimal.h"
#include "GameFramework/Actor.h"
#include "RuinBreakWall.generated.h"
class UStaticMeshComponent;
UCLASS()
class LASTSTANCEUNREAL_API ARuinBreakWall : public AActor
{
    GENERATED_BODY()
public:
    ARuinBreakWall();
    void Shatter(const FVector& Impulse);
private:
    UPROPERTY() TArray<UStaticMeshComponent*> Chunks;
    bool bShattered = false;
};
