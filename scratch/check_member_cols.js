const { PrismaClient } = require('@prisma/client');
const prisma = new PrismaClient();

async function main() {
  const cols = await prisma.$queryRawUnsafe("SELECT column_name, data_type FROM information_schema.columns WHERE table_name = 'members' ORDER BY ordinal_position");
  console.log(cols);
}

main().catch(console.error).finally(() => prisma.$disconnect());
