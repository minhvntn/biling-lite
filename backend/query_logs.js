const { PrismaClient } = require('@prisma/client');
const prisma = new PrismaClient();

async function main() {
  const logs = await prisma.eventLog.findMany({
    orderBy: { createdAt: 'desc' },
    take: 15,
  });
  console.dir(logs, { depth: null });
}

main().finally(() => prisma.$disconnect());
